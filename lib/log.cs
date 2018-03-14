using System;
using Jungo.wdapi_dotnet;

using BYTE = System.Byte;
using WORD = System.UInt16;
using DWORD = System.UInt32;
using UINT32 = System.UInt32;
using UINT64 = System.UInt64;

namespace Jungo.pcie_lib
{
    public class Log//log��
    {
        public delegate void TRACE_LOG(string str);
        public delegate void ERR_LOG(string str);

        public static TRACE_LOG dTraceLog;//׷�ٴ�����ָ��
        public static ERR_LOG dErrLog;//��������ָ��

        public Log(TRACE_LOG funcTrace, ERR_LOG funcErr)//���캯�� ������Ǻ���ָ��
        {
            dTraceLog = funcTrace;//��ֵ
            dErrLog = funcErr;
        }

        public static void TraceLog(string str)  //����׷�����log
        {
            if(dTraceLog == null)
                return;

            dTraceLog(str);
        }

        public static void ErrLog(string str)//���ô������log
        {
            if(dErrLog == null)
                return;

            dErrLog(str);
        }
    }
} 
 


