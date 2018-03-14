using System;
using Jungo.wdapi_dotnet;

using BYTE = System.Byte;
using WORD = System.UInt16;
using DWORD = System.UInt32;
using UINT32 = System.UInt32;
using UINT64 = System.UInt64;

namespace Jungo.pcie_lib
{
    public class Log//log类
    {
        public delegate void TRACE_LOG(string str);
        public delegate void ERR_LOG(string str);

        public static TRACE_LOG dTraceLog;//追踪处理函数指针
        public static ERR_LOG dErrLog;//报错处理函数指针

        public Log(TRACE_LOG funcTrace, ERR_LOG funcErr)//构造函数 传入的是函数指针
        {
            dTraceLog = funcTrace;//赋值
            dErrLog = funcErr;
        }

        public static void TraceLog(string str)  //调用追踪输出log
        {
            if(dTraceLog == null)
                return;

            dTraceLog(str);
        }

        public static void ErrLog(string str)//调用错误输出log
        {
            if(dErrLog == null)
                return;

            dErrLog(str);
        }
    }
} 
 


