using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Mcs.Usb;
using System.Text.RegularExpressions;
using Closed_loop_feedback_gui;

namespace MCS_USB_Windows_Forms_Application1
{
    public partial class Dacq : Form
    {
        CMcsUsbListEntryNet RawPort;
        CMcsUsbListEntryNet DspPort;

        const int Channels = 64;
        const int AnalogChannels = 8;
        const int Checksum = 2;

        private bool use_digital_in = true;

        private int TotalChannels = 0;

        const uint LockMask = 64;

        int Samplerate = 20000;

        int maxAmplitudeValue = 10;
        uint stimThresholdValue = 100000;
        float proportionalGain = 0;

        int? minYValue = null;
        int? maxYValue = null;

        bool RandomStimOn = false;

        string RecordingFilename = null;
        string AmplitudeRecordingFilename = null;
        int AmplitudeRecordingSamplerate = 200;

        List<int> AmplitudeSaveBuffer = new List<int>();

        // for W2100
        private int other_receiver = 0;
        private bool hsSamplingActive = false;

        public CMcsUsbListEntryNet selectedUsbDevice
        {
            get { return (CMcsUsbListEntryNet) cbDeviceList.SelectedItem; }
        }

        CMcsUsbListNet UsbDeviceList = new CMcsUsbListNet(DeviceEnumNet.MCS_DEVICE_USB);
        CMeaUSBDeviceNet mea = new CMeaUSBDeviceNet();

        public Dacq()
        {
            InitializeComponent();
            UsbDeviceList.DeviceArrival += new OnDeviceArrivalRemoval(devices_DeviceArrival);
            UsbDeviceList.DeviceRemoval += new OnDeviceArrivalRemoval(devices_DeviceRemoval);
            SearchDevice();
            InitDialog();

            startDacq.Enabled = true;
            stopDacq.Enabled = false;
            feedbackChannelComboBox.SelectedIndex = 0;
            
            mea.ChannelDataEvent += new OnChannelData(mea_ChannelDataEvent);
        }


        void devices_DeviceRemoval(CMcsUsbListEntryNet entry)
        {
            SearchDevice();
        }

        void devices_DeviceArrival(CMcsUsbListEntryNet entry)
        {
            SearchDevice();
        }

        private void SearchDevice()
        {
            RawPort = null;
            DspPort = null;

            rawSerial.Text = "not found";
            dspSerial.Text = "not found";

            cbDeviceList.Items.Clear();
            for (uint i = 0; i < UsbDeviceList.Count; i++) // loop through number of devices found
            {
                if (UsbDeviceList.GetUsbListEntry(i).SerialNumber.EndsWith("A")) // check for each device if serial number ends with "A" (USB 1) This USB interface will be used by MC_Rack / Multi Channel Experimenter
                {
                    RawPort = UsbDeviceList.GetUsbListEntry(i); // assign to RawPort "handle"
                    rawSerial.Text = RawPort.SerialNumber;
                }
                if (UsbDeviceList.GetUsbListEntry(i).SerialNumber.EndsWith("B"))// check for each device if serial number ends with "B" (USB 2) This USB interface will be used to control DSP
                {
                    DspPort = UsbDeviceList.GetUsbListEntry(i);  // assign to DSPPort "handle"
                    dspSerial.Text = DspPort.SerialNumber;
                }
                cbDeviceList.Items.Add(UsbDeviceList.GetUsbListEntry(i));
            }
            if (cbDeviceList.Items.Count > 1)
            {
                cbDeviceList.SelectedIndex = 1;
            } else if (cbDeviceList.Items.Count > 0)
            {
                cbDeviceList.SelectedIndex = 0;
            }

            if (RawPort != null && DspPort != null)
            {
                btnDeviceOK.BackColor = Color.LawnGreen;
            }
            else if (RawPort != null || DspPort != null)
            {
                btnDeviceOK.BackColor = Color.Yellow;
            }
            else
            {
                btnDeviceOK.BackColor = Color.Red;
            }
            

            // Set Filters
            //CMcsUsbFactoryNet factorydev = new CMcsUsbFactoryNet(); // Create object of class CMcsUsbFactoryNet (provides firmware upgrade and register access capabilities)
            //if (DspPort != null && factorydev.Connect(DspPort, LockMask) == 0)  // if connect call returns zero, connect has been successful
            //{
            //    //double[] xcoeffs;
            //    //double[] ycoeffs;
            //    //mkfilterNet.mkfilter("Bu", 0, "Lp", 2, 1000.0 / (float)Samplerate, 0, out xcoeffs, out ycoeffs);
            //    //factorydev.WriteRegister(0x600, DoubleToFixedInt(1, 16, 30, xcoeffs[0])); // set b[0] fpr 30 Hz LP
            //    //factorydev.WriteRegister(0x608, DoubleToFixedInt(1, 15, 30, xcoeffs[1])); // set b[1] fpr 30 Hz LP
            //    //factorydev.WriteRegister(0x60C, DoubleToFixedInt(1, 30, 30, ycoeffs[1])); // set a[1] fpr 30 Hz LP
            //    //factorydev.WriteRegister(0x610, DoubleToFixedInt(1, 16, 30, xcoeffs[2])); // set b[2] fpr 30 Hz LP
            //    //factorydev.WriteRegister(0x614, DoubleToFixedInt(1, 30, 30, ycoeffs[2])); // set a[2] fpr 30 Hz LP
            //    //factorydev.WriteRegister(0x61C, 0x00000001); // enable

            //    //mkfilterNet.mkfilter("Bu", 0, "Hp", 2, 1.0 / 50000.0, 0, out xcoeffs, out ycoeffs);
            //    //factorydev.WriteRegister(0x620, DoubleToFixedInt(1, 16, 30, xcoeffs[0])); // set b[0] fpr 100 Hz HP
            //    //factorydev.WriteRegister(0x628, DoubleToFixedInt(1, 15, 30, xcoeffs[1])); // set b[1] fpr 100 Hz HP
            //    //factorydev.WriteRegister(0x62C, DoubleToFixedInt(1, 30, 30, ycoeffs[1])); // set a[1] fpr 100 Hz HP
            //    //factorydev.WriteRegister(0x630, DoubleToFixedInt(1, 16, 30, xcoeffs[2])); // set b[2] fpr 100 Hz HP
            //    //factorydev.WriteRegister(0x634, DoubleToFixedInt(1, 30, 30, ycoeffs[2])); // set a[2] fpr 100 Hz HP
            //    //factorydev.WriteRegister(0x63C, 0x00000001); // enable

            //    factorydev.Disconnect();
            //}
        }

        static uint DoubleToFixedInt(int vk, int nk, int commaPos, double valF)
        {
            valF *= 1 << nk;
            if (valF > 0)
            {
                valF += 0.5;
            }
            else
            {
                valF -= 0.5;
            }
            ulong mask = ((ulong)1 << (vk + nk + 1)) - 1;
            ulong val = (ulong)valF;
            uint value = (uint)(val & mask);
            if (commaPos > nk)
            {
                value = value << (commaPos - nk);
            }

            return value;
        }

        void InitDialog()
        {
            string text;
            for (int i = 0; i < Channels; i++)
            {
                text = "Channel " + (i + 1).ToString();
                series0Channel.Items.Add(text);
                series1Channel.Items.Add(text);
            }

            for (int i = 0; i < AnalogChannels; i++)
            {
                text = "ANA " + (i + 1).ToString();
                series0Channel.Items.Add(text);
                series1Channel.Items.Add(text);
            }

            for (int i = 0; i < Channels; i++)
            {
                text = "DSP " + (i + 1).ToString();
                series0Channel.Items.Add(text);
                series1Channel.Items.Add(text);
            }

            if (use_digital_in)
            {
                text = "Digital In";
                series0Channel.Items.Add(text);
                series1Channel.Items.Add(text);
            }

            for (int i = 0; i < Checksum; i++)
            {
                text = "Checksum " + (i + 1).ToString();
                series0Channel.Items.Add(text);
                series1Channel.Items.Add(text);
            }

            series0Channel.SelectedIndex = 0;
            series1Channel.SelectedIndex = 1;

            stimulationFrequencyComboBox.DataSource = new BindingSource(pulseParameters.blankDurations, null);
            stimulationFrequencyComboBox.DisplayMember = "Key";
            stimulationFrequencyComboBox.ValueMember = "Key";
            stimulationFrequencyComboBox.SelectedValue = pulseParameters.defaultFrequency;

        }

        private void startDacq_Click(object sender, EventArgs e)
        {
            if (selectedUsbDevice == null)
            {
                MessageBox.Show("No device selected!");
                return;
            }

            startDacq.Enabled = false;
            stopDacq.Enabled = true;
            SaveToFileCheckBox.Enabled = false;

            string DateTimeNow = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
            if (SaveToFileCheckBox.Checked)
            {
                RecordingFilename = DateTimeNow + "-data.bin";
            }
            AmplitudeRecordingFilename = DateTimeNow + "-amplitude.txt";

            other_receiver = 0;
            if (selectedUsbDevice.SerialNumber.EndsWith("-B"))
            {
                other_receiver = 4; // bit 0/1 select the timeslot of: bit 2/3 = 0 receiver according to USB port, 1 receiver A, 2 receiver B
            }

            uint status;
            if (mea.IsConnected())
                status = 0;
            else 
                status = mea.Connect(selectedUsbDevice, 63);
            if (status == 0)
            {
                int ChannelsInBlock;

                mea.SetDataMode(DataModeEnumNet.Signed_32bit, 0);

                mea.SetNumberOfAnalogChannels(Channels, 0, Channels, AnalogChannels, 0); // Read raw data

                try
                {
                    mea.SetSamplerate(Samplerate, 1, 0);
                }
                catch (CUsbExceptionNet)
                {
                    Samplerate = mea.GetSamplerate(0);
                }

                mea.EnableDigitalIn((use_digital_in ? (DigitalDatastreamEnableEnumNet) 3 : 0), 0);

                // map feedback bit 0 to digital(in) stream bit 4
                mea.SetDigitalSource(DigitalTargetEnumNet.Digstream, 4, W2100DigitalSourceEnumNet.Feedback, 0);
                mea.SetDigitalSource(DigitalTargetEnumNet.Digout, 0, W2100DigitalSourceEnumNet.Feedback, 0);

                mea.EnableChecksum(true, 0);
                ChannelsInBlock = mea.GetChannelsInBlock(0);

                mea.GetChannelLayout(out int analogChannels, out int digitalChannels, out int checksumChannels, out int timestampChannels, out int channelsInBlock, 0);

                TotalChannels = channelsInBlock / 2;
                mea.SetSelectedData(TotalChannels, Samplerate * 10, Samplerate, SampleSizeNet.SampleSize32Signed, ChannelsInBlock);

                mea.ChannelBlock_SetCheckChecksum((uint) checksumChannels, (uint) timestampChannels);
                mea.StartDacq();
                if (mea.GetDeviceId().IdProduct == ProductIdEnumNet.W2100)
                {
                    CW2100_FunctionNet func = new CW2100_FunctionNet(mea);
                    func.SetHeadstageSamplingActive(true, other_receiver + 0);
                    hsSamplingActive = func.GetHeadstageSamplingActive(other_receiver + 0);
                }
                Task.Run(() => SaveStimAmplitudeToFile());
            }
            else
            {
                MessageBox.Show(CMcsUsbNet.GetErrorText(status));
                startDacq.Enabled = true;
                stopDacq.Enabled = false;
                SaveToFileCheckBox.Enabled = true;
            }
        }

        private void stopDacq_Click(object sender, EventArgs e)
        {
            SaveToFileCheckBox.Enabled = true;
            RecordingFilename = null;
            AmplitudeRecordingFilename = null;
            if (mea.GetDeviceId().IdProduct == ProductIdEnumNet.W2100)
            {
                CW2100_FunctionNet func = new CW2100_FunctionNet(mea);
                if (hsSamplingActive)
                {
                    func.SetHeadstageSamplingActive(false, other_receiver + 0);
                }
            }

            startDacq.Enabled = true;
            stopDacq.Enabled = false;

            mea.StopDacq();
            mea.Disconnect();
        }

        private void SaveDataToFile(int[] data, string file, int numFrames)
        {
            if (SaveToFileCheckBox.Checked)
            {
                using (BinaryWriter writer = new BinaryWriter(File.Open(file, FileMode.Append, FileAccess.Write, FileShare.None)))
                {
                    for (int i = 0; i < numFrames; i += 1)
                    {
                        // Save the four recording channels
                        for (int j = 0; j < 4; j++)
                        {
                            writer.Write(data[i * TotalChannels + j]);
                        }
                        // Save MonitorData
                        for (int j = 0; j < 22; j++)
                        {
                            writer.Write(data[i * TotalChannels + Channels + AnalogChannels + j]);
                        }
                    }
                }
            }
        }


        private void SaveStimAmplitudeToFile()
        {
            string filename = "";
            double samplerateRatio = Samplerate / AmplitudeRecordingSamplerate;
            int skip = (int)Math.Floor(samplerateRatio);
            if (AmplitudeRecordingFilename != null)
            {
                filename = AmplitudeRecordingFilename;
                using (StreamWriter file = new StreamWriter(filename, append: true))
                {
                    file.WriteLine("--- START RECORDING: " + DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss.fff") + " ---");
                }
            }
            while (AmplitudeRecordingFilename != null)
            {
                int len = AmplitudeSaveBuffer.Count();
                //Console.WriteLine(len.ToString());
                //Console.WriteLine(skip.ToString());
                if (len > skip)
                {
                    int len_truncated = (int) Math.Floor((double) (len / skip)) * skip;
                    //Console.WriteLine(len_truncated.ToString());
                    int[] values = new int[len];
                    AmplitudeSaveBuffer.CopyTo(0, values, 0, len_truncated);
                    AmplitudeSaveBuffer.RemoveRange(0, len_truncated);
                    using (StreamWriter file = new StreamWriter(filename, append: true))
                    {
                        for (int i=0; i<len_truncated; i+=skip)
                        {
                            int value = values[i];
                            file.WriteLine(value.ToString());
                        }
                    }
                }
                Thread.Sleep(2000);
            }
            if (!String.IsNullOrEmpty(filename))
            {
                using (StreamWriter file = new StreamWriter(filename, append: true))
                {
                    int final_len = AmplitudeSaveBuffer.Count;
                    if (final_len > skip)
                    {
                        for (int i = 0; i < final_len; i += skip)
                        {
                            int value = AmplitudeSaveBuffer.ElementAt(i);
                            file.WriteLine(value.ToString());
                        }
                    }
                    file.WriteLine("--- END RECORDING: " + DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss.fff") + " ---");
                }
            }
            AmplitudeSaveBuffer.Clear();
        }

        void mea_ChannelDataEvent(CMcsUsbDacqNet dacq, int CbHandle, int numFrames)
        {
            //Debug.WriteLine("data available " + numFrames);

            if (numFrames >= Samplerate)
            {
                int[] rawData = mea.ChannelBlock_ReadFramesI32(0, numFrames, out int frames_read);
                int[] data = new int[numFrames * TotalChannels];
                Array.Copy(rawData, 0, data, 0, numFrames * TotalChannels);

                this.Invoke((MethodInvoker)delegate { SaveDataToFile(data, RecordingFilename, numFrames); });
                for (int i = 0; i < TotalChannels; i++)
                {
                    if (data[i] > 10e6 || data[i] < -10e6)
                    {
                        data[i] = data[i + TotalChannels];
                    }
                    int finalFrameIndex = (numFrames - 1) * TotalChannels + i;
                    if (data[finalFrameIndex] > 10e6 || data[finalFrameIndex] < -10e6)
                    {
                        data[finalFrameIndex] = data[finalFrameIndex - TotalChannels];
                    }
                }
                for (int i = TotalChannels; i < (numFrames - 1) * TotalChannels; i++)
                {
                    if (data[i] > 10e6 || data[i] < -10e6)
                    {
                        data[i] = (data[i - TotalChannels] + data[i + TotalChannels]) / 2;
                    }
                }
                int amplitudeChannel = 11;
                for (int i = 0; i < numFrames; i++)
                {
                    AmplitudeSaveBuffer.Add(data[i * TotalChannels + Channels + AnalogChannels + amplitudeChannel]);
                }
                BeginInvoke(new DisplayDataAction(DisplayData), data);
                Thread.Sleep(250);
            }
            else
            {
                int[] data = mea.ChannelBlock_ReadFramesI32(0, numFrames, out int frames_read);
            }
        }

        delegate void DisplayDataAction(int[] data);

        void DisplayData(int[] data)
        {
            dspData.Series[0].Points.Clear();
            dspData.Series[1].Points.Clear();

            double multiplierSeries0 = 1;
            double multiplierSeries1 = 1;

            if (DisplayInMicrovoltsChannel1.Checked) multiplierSeries0 = 0.381;
            if (DisplayInMicrovoltsChannel2.Checked) multiplierSeries1 = 0.381;

            int min = int.MaxValue;
            int max = int.MinValue;

            // the chart can not handle every datapoint
            //for (int i = 0; i < Samplerate; i += 1) // show each data point
            //for (int i = 0; i < Samplerate / 5; i += 20) // show only 1/10 data points
            for (int i = 0; i < Samplerate; i += 100) // show only each 100th data points
            {
                if (cbChart1.Checked)
                {
                    AddPoint(0, i, multiplierSeries0 * data[i * TotalChannels + series0Channel.SelectedIndex], ref min, ref max);
                }
                if (cbChart2.Checked)
                {
                    AddPoint(1, i, multiplierSeries1 * data[i * TotalChannels + series1Channel.SelectedIndex], ref min, ref max);
                }
            }

            if (min <= max)
            {
                dspData.ChartAreas[0].AxisY.Minimum = (minYValue ?? min) - 5;
                dspData.ChartAreas[0].AxisY.Maximum = (maxYValue ?? max) + 5;
            }

        }

        private void AddPoint(int series, int i, double data, ref int min, ref int max)
        {
            dspData.Series[series].Points.AddXY((double)i / Samplerate, data);
            if (data < min) min = (int) Math.Floor(data);
            if (data > max) max = (int) Math.Ceiling(data);
        }

        private void UploadStimulationPatternsToHS(CMcsUsbListEntryNet port, int delta_DBS_amp, int stim_frequency, out string uploadErrorMessage)
        {
            uploadErrorMessage = "";

            other_receiver = 0;
            if (port.SerialNumber.EndsWith("-B"))
            {
                other_receiver = 4; // bit 0/1 select the timeslot of: bit 2/3 = 0 receiver according to USB port, 1 receiver A, 2 receiver B
            }

            uint status;
            if (mea.IsConnected())
                status = 0;
            else
                status = mea.Connect(port);
            if (status == 0)
            {
                mea.SetDataMode(DataModeEnumNet.Signed_32bit, 0);
                mea.SetNumberOfAnalogChannels(Channels, 0, Channels, AnalogChannels, 0); // Read raw data

                try
                {
                    mea.SetSamplerate(Samplerate, 1, 0);
                }
                catch (CUsbExceptionNet)
                {
                    Samplerate = mea.GetSamplerate(0);
                }

                CW2100_FunctionNet func = new CW2100_FunctionNet(mea);
                CW2100_StimulatorFunctionNet stim = new CW2100_StimulatorFunctionNet(mea);

                hsSamplingActive = func.GetHeadstageSamplingActive(other_receiver + 0);

                int preplegth = 0;
                
                stim.SelectTimeSlot(other_receiver + 0);
                
                // Define the amplitude vector of the 3 segments of the biphasic pulse (in nA)
                int[] ampl = new[] { 10000, -10000, 0 };

                // Define the duraion vector of the 3 segments of the biphasic pulse (in us)
                ulong positivePulseDuration = 80;
                ulong negativePulseDuration = 80;
                ulong blankDuration = pulseParameters.blankDurations[stim_frequency];

                ulong[] dur = new ulong[] {positivePulseDuration, negativePulseDuration, blankDuration };
                //ulong[] dur = new ulong[] { 80, 80, 7600 };

                func.SetHeadstageSamplingActive(false, other_receiver + 0);
                // Define and upload each stimulation pattern
                for (int i = 0; i < 16; i++)
                {
                    // Define the amplitude (nA) of each of the 3 segments
                    ampl[0] = delta_DBS_amp * (i + 1);
                    ampl[1] = -delta_DBS_amp * (i + 1);

                    // choose, if global repeat is desired
                    // Define the associated pulse
                    CStimulusFunctionNet.StimulusDeviceDataAndUnrolledData prep = stim.PrepareData(0, ampl, dur, STG_DestinationEnumNet.channeldata_current, 1);

                    // Check the available memory in the headstage  
                    if (i == 0)
                    {
                        preplegth = prep.DeviceDataLength;
                    }

                    // Check that the pulse fits into the designated memory
                    Debug.Assert(preplegth == prep.DeviceDataLength);
                    Debug.Assert(prep.DeviceDataLength <= 15);

                    // Store pulse into designated memory
                    try
                    {
                        stim.SendPreparedData(0x10 * i + 0, prep, STG_DestinationEnumNet.channeldata_current);
                    }
                    catch (CUsbExceptionNet ex)
                    {
                        uploadErrorMessage += String.Format("Error while uploading stim pattern {0}: {1}", i.ToString(), ex.Message) + "\n";
                    }
                }
                func.SetHeadstageSamplingActive(true, other_receiver + 0);
            } else
            {
                MessageBox.Show(CMcsUsbNet.GetErrorText(status));
            }

            // Do not disconnect if data plotting is running
            if (startDacq.Enabled)
            {
                mea.Disconnect();
            }
        }


        delegate void UploadDSBBinaryAction(CMcsUsbListEntryNet port);


        void UploadDSBBinary(CMcsUsbListEntryNet port)
        {
            uint selectedChannelValue = parseSelectedChannel(feedbackChannelComboBox.GetItemText(feedbackChannelComboBox.SelectedItem));
            // Define the upper and lower bounds for the controller output
            int MaxValue = maxAmplitudeValue * 1000;                //in nA
            const int MinValue = 0;                     //in nA

            // Define the step between pulses amplitude
            int delta_DBS_amp = (MaxValue - MinValue) / 16;    // in nA
            int stimFrequency = (int)stimulationFrequencyComboBox.SelectedValue;

            UploadStimulationPatternsToHS(port, delta_DBS_amp, stimFrequency, out string uploadErrorMessage);
            
            if (String.IsNullOrEmpty(uploadErrorMessage))
            {
                CMcsUsbFactoryNet factorydev = new CMcsUsbFactoryNet();
                if (factorydev.Connect(port, LockMask) == 0)
                {
                    // uint stimThresholdDigits = (uint)Math.Floor((float)stimThresholdValue / 0.381);
                    uint stimThresholdDigits = (uint)Math.Floor((float)stimThresholdValue);
                    uint maxAmplitude = (uint)MaxValue;

                    factorydev.WriteRegister(0x1000, stimThresholdDigits);
                    factorydev.WriteRegister(0x1008, maxAmplitude);
                    uint propGainModified = (uint)Math.Floor(proportionalGain * 1000);
                    factorydev.WriteRegister(0x1018, propGainModified);
                    factorydev.WriteRegister(0x1022, selectedChannelValue);

                    string FirmwareFile;
                    FirmwareFile = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    FirmwareFile += @"\..\..\..\..\DSP\FB_W2100_SCU_MEA256\Release\";
                    FirmwareFile += "FB_W2100_SCU_MEA256.bin";

                    factorydev.Disconnect();

                    bool success = factorydev.LoadUserFirmware(FirmwareFile, port, LockMask); // Code for uploading compiled binary
                    if (!success)
                    {
                        MessageBox.Show("Firmware upload failed!");
                    }
                    else
                    {
                        MessageBox.Show("Firmware upload successful!");
                    }
                }
            } else
            {
                MessageBox.Show("The DSP firmware will not be uploaded.\nThe following errors happened during stim parameter upload:\n\n" + uploadErrorMessage);
            }
        }

        static private uint parseSelectedChannel(string selected)
        {
            Regex meanRx = new Regex("^mean$");
            Regex monopolarRx = new Regex("^[1-4]$");
            Regex bipolarRx = new Regex("^([1-4])-([1-4])$");
            uint returnValue = 16;

            if (meanRx.IsMatch(selected))
            {
                returnValue = 16;
            }
            else if (monopolarRx.IsMatch(selected))
            {
                uint channel = uint.Parse(selected) - 1;
                returnValue = channel | (channel << 2);
            }
            else if (bipolarRx.IsMatch(selected))
            {
                Match match = bipolarRx.Match(selected);
                GroupCollection groups = match.Groups;
                uint channel1 = uint.Parse(groups[1].Value) - 1;
                uint channel2 = uint.Parse(groups[2].Value) - 1;
                returnValue = channel1 | (channel2 << 2);
            }
            return returnValue + 32;
        }

        private void UploadDSPBinary_Click(object sender, EventArgs e)
        {
            if (! int.TryParse(MaxAmplitudeTextBox.Text, out maxAmplitudeValue))
            {
                MessageBox.Show("Max amplitude has to be an integer");
            }
            else if (maxAmplitudeValue > 300 || maxAmplitudeValue <= 0)
            {
                MessageBox.Show("Max amplitude has to be between 0 and 300 μA");
            }

            if (!uint.TryParse(StimThresholdTextBox.Text, out stimThresholdValue))
            {
                MessageBox.Show("Max amplitude has to be a positive integer");
            }
            else if (stimThresholdValue < 0)
            {
                MessageBox.Show("Stimulation threshold has to be positive");
            }

            if(! float.TryParse(ProportionalGainInput.Text, out proportionalGain))
            {
                MessageBox.Show("Proportional Gain should be a number with maximum 3 decimal places");
            }

            if (DspPort != null || RawPort != null)
            {
                CMcsUsbListEntryNet port = DspPort;
                if (port == null)
                {
                    port = RawPort;
                }
                BeginInvoke(new UploadDSBBinaryAction(UploadDSBBinary), port);
            }
            else
            {
                MessageBox.Show("No port available");
            }
        }

        private void StopDSP_Click(object sender, EventArgs e)
        {
            CMcsUsbFactoryNet factorydev = new CMcsUsbFactoryNet(); // Create object of class CMcsUsbFactoryNet (provides firmware upgrade and register access capabilities)
            AmplitudeRecordingFilename = null;
            if (DspPort != null || RawPort != null)
            {
                CMcsUsbListEntryNet port = DspPort;
                if (port == null)
                {
                    port = RawPort;
                }

                if (factorydev.Connect(port, LockMask) == 0) // if connect call returns zero, connect has been successful
                {
                    factorydev.Coldstart(CFirmwareDestinationNet.MCU1);
                    factorydev.WriteRegister(0x9A80, 0);
                    factorydev.Disconnect();
                }
            }
            else
            {
                MessageBox.Show("No port available");
            }
        }

        private void StartRandomStimButton_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(MaxAmplitudeTextBox.Text, out maxAmplitudeValue))
            {
                MessageBox.Show("Max amplitude has to be an integer");
            }
            else if (maxAmplitudeValue > 300 || maxAmplitudeValue <= 0)
            {
                MessageBox.Show("Max amplitude has to be between 0 and 300 μA");
            }
            if (!int.TryParse(PercentageOnInputBox.Text, out int PercentageStimOn))
            {
                MessageBox.Show("Percentage has to be an integer");
            }
            else if (PercentageStimOn > 99 || PercentageStimOn < 0)
            {
                MessageBox.Show("Percentage has to be between 0 and 99");
            }

            if (DspPort != null || RawPort != null)
            {
                CMcsUsbListEntryNet port = DspPort;
                if (port == null)
                {
                    port = RawPort;
                }
                PercentageOnInputBox.Enabled = false;
                StopRandomStimButton.Enabled = true;
                Task.Run(() => RandomOnOffStimulation(port));
                //BeginInvoke(new RandomOnOffStimulationAction(RandomOnOffStimulation), port);
            }
            else
            {
                MessageBox.Show("No port available");
            }
        }

        delegate void RandomOnOffStimulationAction(CMcsUsbListEntryNet port);

        void RandomOnOffStimulation(CMcsUsbListEntryNet port)
        {
            int PercentageStimOn = Int32.Parse(PercentageOnInputBox.Text);

            // Define the upper and lower bounds for the controller output
            int MaxValue = maxAmplitudeValue * 1000;                //in nA
            const int MinValue = 0;                     //in nA

            // Define the step between pulses amplitude
            int delta_DBS_amp = (MaxValue - MinValue) / 16;    // in nA
            int stimFrequency = (int) stimulationFrequencyComboBox.SelectedValue;

            UploadStimulationPatternsToHS(port, delta_DBS_amp, stimFrequency, out string uploadErrorMessage);

            if (String.IsNullOrEmpty(uploadErrorMessage))
            {
                Random random = new Random();
                int maxOn = 220;
                int maxOff = Convert.ToInt32(maxOn * (100 - PercentageStimOn) / PercentageStimOn);

                CMcsUsbFactoryNet factorydev = new CMcsUsbFactoryNet(); // Create object of class CMcsUsbFactoryNet (provides firmware upgrade and register access capabilities)
                bool stimOn = false;
                double sleepTime = 0;
                if (factorydev.Connect(port, LockMask) == 0) // if connect call returns zero, connect has been successful
                {
                    RandomStimOn = true;
                    while (RandomStimOn)
                    {
                        if (stimOn)
                        {
                            factorydev.WriteRegister(0x9A80, 0);
                            stimOn = false;
                            sleepTime = random.Next(1, maxOff);
                        }
                        else
                        {
                            factorydev.WriteRegister(0x9A80, 0x1000 * 15 + 0x100);
                            stimOn = true;
                            sleepTime = random.Next(1, maxOn);
                        }
                        Thread.Sleep(Convert.ToInt32(sleepTime));
                    }
                    factorydev.WriteRegister(0x9A80, 0);
                    factorydev.Disconnect();
                    MessageBox.Show("Random Stim Finished");
                }
            } else
            {
                MessageBox.Show("Random Stimulation will not proceed.\nThe following errors were encountered while uploading stimulation patterns to HS:\n\n" + uploadErrorMessage);
            }
        }

            

        private void StopRandomStimButton_Click(object sender, EventArgs e)
        {
            RandomStimOn = false;
            StopRandomStimButton.Enabled = false;
            PercentageOnInputBox.Enabled = true;
        }

        private void ApplyYLimitChangesButton_Click(object sender, EventArgs e)
        {
            if (Int32.TryParse(minYChartInput.Text, out int minY))
            {
                minYValue = minY;
                dspData.ChartAreas[0].AxisY.Minimum = minYValue.Value - 5;
            }
            else
            {
                minYValue = null;
            }
            if (Int32.TryParse(maxYChartInput.Text, out int maxY))
            {
                maxYValue = maxY;
                dspData.ChartAreas[0].AxisY.Maximum = maxYValue.Value - 5;
            }
            else
            {
                maxYValue = null;
            }
        }

        private void OpenDebugFormButton_Click(object sender, EventArgs e)
        {
            Form DebugForm = new DebugControlsForm(this, mea, UsbDeviceList);
            DebugForm.Show();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            int stimFrequency = (int) stimulationFrequencyComboBox.SelectedValue;
            ulong blankDuration = pulseParameters.blankDurations[stimFrequency];
            MessageBox.Show(String.Format("Selected Stim Frequency: {0}; blank duration {1}", stimFrequency, blankDuration));
        }
    }
}
