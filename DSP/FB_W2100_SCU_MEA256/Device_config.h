#ifndef DEVICE_CONFIG_H_
#define DEVICE_CONFIG_H_

#define DEVICE_REGISTER(reg)          (*(volatile Uint32 *)(0xa0000000+(reg<<2)))

#define _W2100
#define INCLUDE_HS1_DATA
//#define INCLUDE_HS2_DATA
#define INCLUDE_IF_DATA
//#define INCLUDE_HS2_FILTER_DATA
//#define INCLUDE_DIGITAL_DATA
//#define INCLUDE_TIMESTAMP_DATA




#ifdef _W2100
#ifdef INCLUDE_HS1_DATA
#define HS1_CHANNELS    48
#define HS1_DATA_ENABLE 0x0000001F
#else
#define HS1_CHANNELS    0
#define HS1_DATA_ENABLE 0
#endif
#else
#ifdef INCLUDE_HS1_DATA
#define HS1_CHANNELS    256
#define HS1_DATA_ENABLE 0xFF
#else
#define HS1_CHANNELS    0
#define HS1_DATA_ENABLE 0
#endif
#endif

#ifdef INCLUDE_HS2_DATA
#define HS2_CHANNELS    256
#define HS2_DATA_ENABLE 1
#else
#define HS2_CHANNELS    0
#define HS2_DATA_ENABLE 0
#endif

#ifdef INCLUDE_IF_DATA
#define IF_CHANNELS     8
#define IF_DATA_ENABLE  1
#else
#define IF_CHANNELS     0
#define IF_DATA_ENABLE  0
#endif


#ifdef INCLUDE_DIGITAL_DATA  // for now, 8 channels of digital data from interfaceboard
#define DIGITAL_CHANNELS    8
#define DIGITAL_DATA_ENABLE 1
#else
#define DIGITAL_CHANNELS    0
#define DIGITAL_DATA_ENABLE 0
#endif

#ifdef INCLUDE_TIMESTAMP_DATA
#define TIMESTAMP_CHANNELS    2
#define TIMESTAMP_DATA_ENABLE 1
#else
#define TIMESTAMP_CHANNELS    0
#define TIMESTAMP_DATA_ENABLE 0
#endif

#define MAX_DATAPOINTS_PER_FRAME	 512

#ifdef _W2100
#define INDATA_CONFIG0_VALUE 0
#define INDATA_CONFIG1_VALUE (HS1_DATA_ENABLE)
#define INDATA_CONFIG2_VALUE (HS2_DATA_ENABLE)
#define INDATA_CONFIG3_VALUE (IF_DATA_ENABLE)
#define INDATA_CONFIG4_VALUE 0
#define INDATA_CONFIG5_VALUE 0
#define INDATA_CONFIG6_VALUE 0
#define INDATA_CONFIG7_VALUE (DIGITAL_DATA_ENABLE)
#define INDATA_CONFIG8_VALUE 0
#define INDATA_CONFIG9_VALUE 0
#define INDATA_CONFIGa_VALUE 0
#define INDATA_CONFIGb_VALUE 0
#define INDATA_CONFIGc_VALUE 0
#define INDATA_CONFIGd_VALUE 0
#define INDATA_CONFIGe_VALUE 0
#define INDATA_CONFIGf_VALUE (TIMESTAMP_DATA_ENABLE)
#else
#define INDATA_CONFIG0_VALUE 0
#define INDATA_CONFIG1_VALUE (HS1_DATA_ENABLE)
#define INDATA_CONFIG2_VALUE 0
#define INDATA_CONFIG3_VALUE 0
#define INDATA_CONFIG4_VALUE 0
#define INDATA_CONFIG5_VALUE (HS2_DATA_ENABLE)
#define INDATA_CONFIG6_VALUE 0
#define INDATA_CONFIG7_VALUE 0
#define INDATA_CONFIG8_VALUE 0
#define INDATA_CONFIG9_VALUE (IF_DATA_ENABLE)
#define INDATA_CONFIGa_VALUE 0
#define INDATA_CONFIGb_VALUE 0
#define INDATA_CONFIGc_VALUE 0
#define INDATA_CONFIGd_VALUE (DIGITAL_DATA_ENABLE)
#define INDATA_CONFIGe_VALUE 0
#define INDATA_CONFIGf_VALUE (TIMESTAMP_DATA_ENABLE)
#endif


#endif /* DEVICE_CONFIG_H_ */
