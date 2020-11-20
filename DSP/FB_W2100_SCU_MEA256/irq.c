#include <csl.h>
#include <cslr_tmr.h>
#include <cslr_gpio.h>
#include <cslr_chip.h>
#include <cslr_edma3cc.h>
#include <soc.h>
#include <c6x.h>

#include "main.h"
#include "irq.h"
#include "Device_lib.h"
#include "Common/MCS_USB_commands.h"

int num_tr_cross[HS1_CHANNELS/2];
int last_tr_cross[HS1_CHANNELS/2];

Uint32 aux_value = 0;

#define DATA_HEADER_SIZE 1

void toggleLED()
{
    static int led = 0;
    CSL_GpioRegsOvly gpioRegs = (CSL_GpioRegsOvly)CSL_GPIO_0_REGS;

    CSL_FINS(gpioRegs->OUT_DATA, GPIO_OUT_DATA_OUT2, led); // LED
    led = 1 - led;
}


void W2100Usb(Uint32 direction, Uint32 request, Uint32 value, Uint32 index, Uint32 data, Uint32 length)
{
	Uint32 setup0 = direction + (request << 8) + (value << 16);
	Uint32 setup1 = index + (length << 16);
	WRITE_REGISTER(0x0c10, setup0);
	WRITE_REGISTER(0x0c11, setup1);
	WRITE_REGISTER(0x0c12, data);
	WRITE_REGISTER(0x0c13, 0);
	Uint32 ready = 0;
	Uint32 repeat = 50000; // 2.5s
	while (!ready && repeat > 0)
	{
		repeat--;
		ready = READ_REGISTER(0x0c13);
	}
}

// Mailbox write interrupt
// use "#define USE_MAILBOX_IRQ" in global.h to enable this interrupt
interrupt void interrupt8(void)
{
    int update_waveform = 0;
    Uint32 reg_written;
    Uint32 reg_value;

    // a write to a mailbox register occurred
	reg_written = READ_REGISTER(MAILBOX_LASTWRITTEN);
	reg_value   = READ_REGISTER(MAILBOX_BASE + reg_written);

	switch(reg_written)
	{
	case MAILBOX_THRSHOLD:
	    threshold = reg_value;
	    break;

	case MAILBOX_DEADTIME:
	    deadtime = reg_value;
	    break;

    case MAILBOX_AMPLITUDE:
        if (StimAmplitude != reg_value)
        {
            StimAmplitude = reg_value;
            update_waveform = 1;
        }
        break;

    case MAILBOX_DURATION:
        if (StimDuration != reg_value)
        {
            StimDuration    = reg_value;
            update_waveform = 1;
        }
        break;

    case MAILBOX_REPEATS:
        if (StimRepeats != reg_value)
        {
            StimRepeats   = reg_value;
            update_waveform = 1;
        }
        break;

    case MAILBOX_STEPSIZE:
        if (StimStepsize != reg_value)
        {
            StimStepsize  = reg_value;
            update_waveform = 1;
        }
        break;
	}

	if (update_waveform)
	{
	    UploadBiphaseRect(0, 0, StimAmplitude, StimDuration, StimRepeats);
	}
}


// DMA finished Interrupt
interrupt void interrupt6(void)
{
	// Define sampling frequency and period
	const float f_s = 20000;
	const float T_s = 1 / f_s;

	// Define controller call period (20ms)
	const float T_controller = 0.02; 

	// Calculate T_controller to T_s ratio to know every how many calls of interrupt 6 we need to call the controller
	const int ratio_T_controller_T_s = 1000;  //5 * 18 / 1000 * f_s;

	// Define SetPoint being the target beta ARV 
    const float SetPoint = 7;                     //set SetPoint randomly to be 5mV ie 5000uV
    
	// Define a variable that is true just the first run
    static int first_run = 1; 

    // Define the upper and lower bounds for the controller output (i.e. DBS amplitude)
    const float MaxValue = 300;                     //in uA
    const float MinValue = 0;                       //in uA

    // Define the step between pulses amplitude
    const float delta_DBS_amp = ( MaxValue - MinValue ) / 15;

    // Define vector to store the amplitude of the stimulation pulses
    static float pulse[16] = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15};

    // Define dummy variable
    static int c = 0;

    // Multiply pulse vector by delta_DBS_amp to get the vector assoicated with the amplitude of the stimulation pulses
    if (first_run)
    {
        for (c = 0; c < 16; c++)
        {
            pulse[c] = pulse[c] * delta_DBS_amp;
        }
        // Not anymore the first run (thus, this if statement will be run only in the first call of interrupt6 function)
        first_run = 0;
	}

    // Define current error
    static float error = 0;

    // Define the applied bounded controller output value (i.e. applied pulse amplitude)
    static float OutputValue = 0;

    // Set increment initially to 0
    static float increment = 0.0;

    // Define index of stimulation pulse to be applied
    static int stim_index = 0;

    // Define difference between the amplitude of the required stimulation pulse and the one to be applied
    static float pulse_amp_diff = 0;

    // Define state value
    static float state_value = 0;
    
	// Define counter for function run
	static int timestamp = 0; // exists only in this function but is created only once on the first function call (i.e. static)
	static int segment = 0;
	static int crossing_detected = 0;
	
	static int stg_electrode = 0;

	static int seg = 0;

	int i;
	CSL_Edma3ccRegsOvly edma3ccRegs = (CSL_Edma3ccRegsOvly)CSL_EDMA3CC_0_REGS;
	
	// search start of data segments
	Int32  HS_data_header[NUM_SEGMENTS];
    // command information decoding
    // bits 31:28 Data Source/Group
    // bits 27:21 reserved for future use
    // bit 20:19 data format '00': 2x16 bit, '01': 1x32 bit, '10': 64 bit in 2 DWords
    // bit 18 '0': Analog data, '1': Digital data / don't alow data manipulation (no filter, no sign/unsign change)
    // bit 17 last Frame/Segment of sweep
    // bit 16 first Frame/Segment of sweep
#ifdef _W2100
    // bits 15:8 segment number / sub group
    // bits 7:0 length of this packet excluding token counter/CRC
#else
    // bits 15:9 segment number / sub group
    // bits 8:0 length of this packet excluding token counter/CRC
#endif

    // Data Source/Group decoding:
    // 0: Header
#ifdef _W2100
    // 1: Receiver 0 Data (up to 32 segments, each segment has up to 255 32-bit words)
    // 2: Receiver 1 Data (up to 32 segments, each segment has up to 255 32-bit words)
    // 3: IFB Analog Data
    // 4: DSP Data (up to 128 segments, each segment has up to 256 32-bit words)
    // 7: Digital Data
#else
    // 1,2,3,4: Extender Unit 0 Data (up to 4*16 segments, each segment has up to 511 32-bit words)
    // 5,6,7,8: Extender Unit 1 Data (up to 4*16 segments, each segment has up to 511 32-bit words)
    // 9: IFB Analog Data
    // A: Analog data
    // B: DAC data
    // C: DSP Data (up to 128 segments, each segment has up to 256 32-bit words)
    // D: Digital Data
#endif
    // E: reserved
    // F: Fooder/Tail

	Int32* restrict HS_Data_p[NUM_SEGMENTS];
	Int32* restrict IF_Data_p  = 0;
	int index = 0;
	for(i = 0;i < NUM_SEGMENTS;i++)
	{
		HS_data_header[i] = MeaData[index];
		HS_Data_p[i] = (Int32 *)&MeaData[index + 1];
#ifdef _W2100
		if ((HS_data_header[i] & 0xF0000000) == 0x30000000) // IF Analog
#else
		if ((HS_data_header[i]& 0xF0000000) == 0x90000000) // IF Analog
#endif
		{
			IF_Data_p =  (Int32 *)&MeaData[index + 1];
		}
        // debug
		// MonitorData[i] = MeaData[index];

#ifdef _W2100
		index += (HS_data_header[i] & 0xFF) + 1;
#else
		index += (HS_data_header[i] & 0x1FF) + 1;
#endif
	}

	// Prepare DMA for next data transfer DO NOT CHANGE THE FOLLOWING LINE
	CSL_FINST(edma3ccRegs->ICRH, EDMA3CC_ICRH_I52, CLEAR);	// Clear pending interrupt for event 52
    // 
    
	// Write to AUX register to see how long interrupt takes (set output to high, at the end set output to low)

	//if (timestamp == 9999)
	{
		aux_value |= 1;
	}
	//else
	{
	//	aux_value &= ~1;
	}
	///// WRITE_REGISTER(IFB_AUX_OUT, aux_value); // set  AUX 1 to value one

#ifndef _W2100
	// Monitor Analog in
    if (IF_Data_p[0] > threshold)
    {
        aux_value |= 2;
        WRITE_REGISTER(0x002C, 0x404); //switch on HS2 LED

        if (crossing_detected == 0)
        {
            crossing_detected = 1;
            WRITE_REGISTER(TRIGGER_ID_HS1,        segment << 16);  // select segment for trigger 1
            WRITE_REGISTER(TRIGGER_SET_EVENT_HS1, 0x01);     // Start Trigger 1
        }
    }
    else
    {
        crossing_detected = 0;
        aux_value &= ~2;
        WRITE_REGISTER(0x002C, 0x400); //switch on HS2 LED
    }

    // once per second
    if (timestamp == 49999)
    {    	
    	int enable;
    	int mux;
    	int config;
    	int iMeanActivity = 0;
    	for (i = 0; i < HS1_CHANNELS; i++)
    	{
    		iMeanActivity = iMeanActivity + num_tr_cross[i];
    	}
    	iMeanActivity = iMeanActivity /(HS1_CHANNELS);

    	for (i = 0; i < HS1_CHANNELS / ELECTRODES_PER_REGISTER; i++)
    	{
    	    StimulusEnable[i] = 0;
            elec_config[i] = 0;
    	}

        for (i = 0; i < HS1_CHANNELS / (ELECTRODES_PER_REGISTER/2); i++)
        {
            DAC_select[i] = 0;
        }

        for (i = 0; i < HS1_CHANNELS; i++)
    	{
    		// if (num_tr_cross[i] <= iMeanActivity) {
    		// if (num_tr_cross[i] > 0)
            if (i == stg_electrode)
			{
    			enable = 1;
    			mux = 1; // Stimulation Source is DAC 1
    			config = 0; // Use Sidestream 1 for Stimulation Switch
    		}
    		else
    		{
    			enable = 0;
    			mux = 0; // Keep MUX at ground
    			config = 1; // Keep Switches static, manual mode
    		}
    		
    		// 1 bit per channel
    		StimulusEnable[i/ELECTRODES_PER_REGISTER] |= (enable << (i % ELECTRODES_PER_REGISTER));
            elec_config[i/ELECTRODES_PER_REGISTER]    |= (config << (i % ELECTRODES_PER_REGISTER));

            // 2 bit per channel
            DAC_select[i/(ELECTRODES_PER_REGISTER/2)] |= (mux << 2 * (i % (ELECTRODES_PER_REGISTER/2)));
       	}
       	
       	for (i = 0; i < HS1_CHANNELS / ELECTRODES_PER_REGISTER; i++)
       	{
       		WRITE_REGISTER(STG_ELECTRODE_ENABLE + i*REGISTER_OFFSET, StimulusEnable[i]); // Enable Stimulation on STG
//			WRITE_REGISTER(0x8140+i*REGISTER_OFFSET, StimulusEnable[i]); // Enable hard blanking for Stimulation Electrodes
            WRITE_REGISTER(STG_ELECTRODE_MODE + (i*REGISTER_OFFSET), elec_config[i]); // Configure Stimulation Electrodes to Listen to Sideband 1
       	}

       	for (i = 0; i < HS1_CHANNELS / (ELECTRODES_PER_REGISTER/2); i++)
       	{
       		WRITE_REGISTER(STG_ELECTRODE_MUX + (i*REGISTER_OFFSET), DAC_select[i]);  // Select DAC 1 for Stimulation Electrodes
       	}

//		WRITE_REGISTER(TRIGGER_ID_HS1,        segment << 16);  // select segment for trigger 1
//		WRITE_REGISTER(TRIGGER_SET_EVENT_HS1, 0x01);     // Start Trigger 1
//		segment = 1 - segment; // alternate between segment 0 and 1
       	
    	// analyze data
    	// configure stim signal
    }


	if (++timestamp == 50000)
	{
	    timestamp = 0;
	    toggleLED();

	    if (stg_electrode < 256)
	    {
	        stg_electrode++;
        }
	    else
	    {
	        stg_electrode = 0;
	    }

	}
#else
#if 1
    // Increment timestamp each function call and call controller when T_controller elapsed i.e. when timestamp ==	ratio_T_controller_T_s
    if (++timestamp ==	ratio_T_controller_T_s)
    {
        // set AUX output to 1
        aux_value |= 1;
	    WRITE_REGISTER(IFB_AUX_OUT, aux_value); 
        
        // reset timestamp counter
        timestamp=0;
        
        // Calculate current beta ARV (differential recording)
        state_value = abs(HS_Data_p[0][2] - HS_Data_p[0][0]); // NB: In uV

        // Calculate Error - if SetPoint > 0.0,
        // then normalize error with respect to SetPoint
        if (SetPoint==0)
        {
            error = state_value - SetPoint;                     //in uV
            increment = 0.0;
        }
        else
        {
            error = (state_value - SetPoint) / SetPoint;        //in uV
            if (error>0)
                increment = delta_DBS_amp;
            else
                increment = -1*delta_DBS_amp;
        }

        // Bound the controller output (between MinValue - MaxValue)
        if ( OutputValue + increment > MaxValue )
            OutputValue = MaxValue;
        else if ( OutputValue + increment < MinValue )
            OutputValue = MinValue;
        else
            OutputValue = OutputValue + increment;
        
        // Determine the pulse closest to OutputValue (and its index)
        // Set randomly the index of the pulse closest to OutputValue to be 0
//        stim_index = 0;

        // Calculate the difference between OutputValue and pulse of index stim_index
        pulse_amp_diff = abs(pulse[stim_index] - OutputValue);

        // Pick the stimulation pulse of amplitude closest to OutputValue
        // Loop around all 16 pulses
        for (c = 1; c < 16; c++)
        {
            // Check if this pulse is the closest to OuputValue
            if ( abs(pulse[c] - OutputValue) < pulse_amp_diff)
            {
                // Update the index of the closest pulse to OutputValue
//                stim_index = c;

                // Update the difference between OutputValue and pulse of index stim_index
                pulse_amp_diff = abs(pulse[c] - OutputValue);
            }
        }
        
        // Relate the pulse index to the memory segment index assoicated with it 
        if (HS_Data_p[0][2] > SetPoint){
            stim_index = (stim_index + 1) % 16;
            seg = stim_index;
        }

        if (seg == 0) {

            // Turn off stimulation if stim_index = 0
            WRITE_REGISTER(0x9A80, 0);
        }
        else {
            // Update stimulation pulse
            WRITE_REGISTER(0x9A80, 0x1000 * seg +  0x100); // Trigger Channel 1
        }
        
        // Set AUX 1 output value to zero
        aux_value &= 0;
	    WRITE_REGISTER(IFB_AUX_OUT, aux_value);

        MonitorData[1] = OutputValue;
        MonitorData[2] = stim_index;
        MonitorData[3] = delta_DBS_amp;
     }

MonitorData[0] = HS_Data_p[0][2];
MonitorData[6] = pulse[0];
MonitorData[7] = pulse[1];
MonitorData[8] = pulse[2];
MonitorData[9] = pulse[3];
MonitorData[10] = pulse[4];
MonitorData[11] = pulse[5];
MonitorData[12] = pulse[6];
MonitorData[13] = pulse[7];
MonitorData[14] = pulse[8];
MonitorData[15] = pulse[9];
MonitorData[16] = pulse[10];
MonitorData[17] = pulse[11];
MonitorData[18] = pulse[12];
MonitorData[19] = pulse[13];
MonitorData[20] = pulse[14];
MonitorData[21] = pulse[15];

/*
#define PERIOD 200
	int nextsegment[16] =
	{
			0, 1, 2, 3,
			2, 1, 0, 2,
			4, 8, 7, 6,
			5, 4, 3, 2,
	};
	static int j = 0;
	if (timestamp == PERIOD - 50)
	{
		WRITE_REGISTER(0x9A80, 0x1000 * nextsegment[j] +  0x100); // Trigger Channel 1
		j++;
	}

	if (timestamp > 0 && timestamp < PERIOD * 16 - 100)
	{
		if (timestamp % PERIOD == 0)
		{
			WRITE_REGISTER(0x9A80, 0x1000 * nextsegment[j] +  0x100); // Trigger Channel 1
			j++;
		}
	}

	if (timestamp == PERIOD * 16 + 50)
	{
		WRITE_REGISTER(0x9A80, 0); // Stop Trigger
	}
	++timestamp;
*/

#else
	if (timestamp == 5000)
	{
	    //WRITE_REGISTER(0x0480, 0); // Feedback
	}
	aux_value &= ~2;
	MonitorData[3] = 0;
	if (++timestamp == 20000)
	{
		timestamp = 0;
	    toggleLED();

	    //WRITE_REGISTER(0x0480, 1); // Feedback
	    seg++;
	    if (seg >= 16)
	    {
	    	seg = 0;
	    }
	    WRITE_REGISTER(0x9A80, 0x1000 * seg +  0x100); // Trigger Channel 1
	    aux_value |= 2;
	    MonitorData[3] = 5;
	}
#endif
#endif

	//int f = HS_Data_p[0][0] > 0;
	//WRITE_REGISTER(0x0480, f); // Feedback
/*
	MonitorData[0] = (timestamp >> 4) & 0xFF;
	MonitorData[1] = HS_Data_p[0][4] + 80;
	MonitorData[2] = (IF_Data_p[0][0] - 1190000) >> 15;
	//MonitorData[3] = HS_Data_p[0][6] + 30;
	MonitorData[4] = HS_Data_p[0][0] >> 10;
	MonitorData[5] = HS_Data_p[0][8] + 30;
	MonitorData[6] = HS_Data_p[0][9] + 30;
*/
	for(i = 0; i < 64; i++)
	{
		//MonitorData[i] = HS_Data_p[0][i];
	}
	for(i = 32; i < 64; i++)
	{
		//MonitorData[i] = IF_Data_p[0][i];
	}

    CSL_FINST(edma3ccRegs->ESRH, EDMA3CC_ESRH_E53, SET);    // Manual Trigger Event 53

	aux_value &= ~1;
	////// WRITE_REGISTER(IFB_AUX_OUT, aux_value); // set AUX 1 to value zero
}


