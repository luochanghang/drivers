using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Data;

using Jungo.wdapi_dotnet;
using wdc_err = Jungo.wdapi_dotnet.WD_ERROR_CODES;
using item_types = Jungo.wdapi_dotnet.ITEM_TYPE;
using UINT64 = System.UInt64;
using UINT32 = System.UInt32;
using DWORD = System.UInt32;
using WORD = System.UInt16;
using BYTE = System.Byte;
using BOOL = System.Boolean;
using WDC_DEVICE_HANDLE = System.IntPtr;   
using HANDLE = System.IntPtr;
namespace Jungo.pcie_lib
{
    /* PCI diagnostics plug-and-play and power management events handler *
     * function type */
    public delegate void USER_EVENT_CALLBACK(ref WD_EVENT pEvent, PCIE_Device dev);//构造方法重载
    /* PCI diagnostics interrupt handler function type */
    public delegate void USER_INTERRUPT_CALLBACK(PCIE_Device device);


    public class PCIE_Device
    {
        private WDC_DEVICE m_wdcDevice = new WDC_DEVICE();   //定义一个内部的wed_DEVICE对象
        protected MarshalWdcDevice m_wdcDeviceMarshaler;
        private USER_EVENT_CALLBACK m_userEventHandler;
        private USER_INTERRUPT_CALLBACK m_userIntHandler;
        private EVENT_HANDLER_DOTNET m_eventHandler;
        private INT_HANDLER m_intHandler;
        protected string m_sDeviceLongDesc;
        protected string m_sDeviceShortDesc;
        private PCIE_Regs m_regs;

#region " constructors " 
        /* constructors & destructors */
        internal protected PCIE_Device(WD_PCI_SLOT slot): this(0, 0, slot){}

        internal protected PCIE_Device(DWORD dwVendorId, DWORD dwDeviceId,//构造函数
            WD_PCI_SLOT slot)
        {
            m_wdcDevice = new WDC_DEVICE();
            m_wdcDevice.id.pciId.dwVendorId = dwVendorId;
            m_wdcDevice.id.pciId.dwDeviceId = dwDeviceId;
            m_wdcDevice.slot.pciSlot = slot;
            m_wdcDeviceMarshaler = new MarshalWdcDevice();
            m_eventHandler = new EVENT_HANDLER_DOTNET(PCIE_EventHandler);
            m_regs = new PCIE_Regs();
            SetDescription();
        } 

        public void Dispose()
        {
            Close();
        }
#endregion

#region " properties " 
        /*********************
         *  properties       *
         *********************/

        public IntPtr Handle//设备的句柄
        {
            get
            {
                return m_wdcDevice.hDev;
            }
            set
            {
                m_wdcDevice.hDev = value;
            }
        }

        protected WDC_DEVICE wdcDevice
        {
            get
            {
                return m_wdcDevice;
            }
            set
            {
                m_wdcDevice = value;
            }
        }

        public WD_PCI_ID id
        {
            get
            {
                return m_wdcDevice.id.pciId;
            }
            set
            {
                m_wdcDevice.id.pciId = value;
            }
        }

        public WD_PCI_SLOT slot    //https://www.cnblogs.com/DannyShi/p/4475726.html 定义属性 用于安全的访问重要的数据 如私有数据
        {
            get
            {
                return m_wdcDevice.slot.pciSlot;
            }
            set
            {
                m_wdcDevice.slot.pciSlot = value;
            }
        }

        public WDC_ADDR_DESC[] AddrDesc
        {
            get
            {
                return m_wdcDevice.pAddrDesc;
            }
            set
            {
                m_wdcDevice.pAddrDesc = value;
            }
        }

        public PCIE_Regs Regs
        {
            get
            {
                return m_regs;
            }
        }

#endregion

#region " utilities " 
        /********************
         *     utilities    *
         *********************/

        /* public methods */

        public string[] AddrDescToString(bool bMemOnly)
        {
            string[] sAddr = new string[AddrDesc.Length];
            for (int i = 0; i<sAddr.Length; ++i)
            {
                sAddr[i] = "BAR " + AddrDesc[i].dwAddrSpace.ToString() + 
                     ((AddrDesc[i].fIsMemory)? " Memory " : " I/O ");

                if (wdc_lib_decl.WDC_AddrSpaceIsActive(Handle, 
                    AddrDesc[i].dwAddrSpace))
                {
                    WD_ITEMS item =
                        m_wdcDevice.cardReg.Card.Item[AddrDesc[i].dwItemIndex];
                    UINT64 dwAddr = (UINT64)(AddrDesc[i].fIsMemory?
                        item.I.Mem.dwPhysicalAddr : item.I.IO.dwAddr);

                    sAddr[i] += dwAddr.ToString("X") + " - " + 
                        (dwAddr + AddrDesc[i].dwBytes - 1).ToString("X") + 
                        " (" + AddrDesc[i].dwBytes.ToString("X") + " bytes)";
                }
                else
                    sAddr[i] += "Inactive address space";
            }
            return sAddr;
        }

        public string ToString(BOOL bLong)
        {
            return (bLong)? m_sDeviceLongDesc: m_sDeviceShortDesc;//装换 使用哪种描述方法
        }

        public bool IsMySlot(ref WD_PCI_SLOT slot)
        {
            if(m_wdcDevice.slot.pciSlot.dwBus == slot.dwBus &&
                m_wdcDevice.slot.pciSlot.dwSlot == slot.dwSlot &&
                m_wdcDevice.slot.pciSlot.dwFunction == slot.dwFunction)
                return true;

            return false;
        }

        /* protected methods */

        protected void SetDescription()
        {
            m_sDeviceLongDesc = string.Format("PCIE Device: Vendor ID 0x{0:X}, " 
                + "Device ID 0x{1:X}, Physical Location {2:X}:{3:X}:{4:X}", 
                id.dwVendorId, id.dwDeviceId, slot.dwBus, slot.dwSlot, 
                slot.dwFunction);

            m_sDeviceShortDesc = string.Format("Device " + 
                "{0:X},{1:X} {2:X}:{3:X}:{4:X}", id.dwVendorId, 
                id.dwDeviceId, slot.dwBus, slot.dwSlot, slot.dwFunction); 
        }

        /* private methods */

        private bool DeviceValidate()  //地址空间是否存在
        {
            DWORD i, dwNumAddrSpaces = m_wdcDevice.dwNumAddrSpaces;

            /* NOTE: You can modify the implementation of this function in     *注意：您可以修改此功能的实施，以验证设备是否具有您期望找到的资源
             * order to verify that the device has the resources you expect to *
             * find */

            /* Verify that the device has at least one active address space */
            for (i = 0; i < dwNumAddrSpaces; i++)
            {
                if (wdc_lib_decl.WDC_AddrSpaceIsActive(Handle, i))  //检查是否有BAR空间
                    return true;
            }

            Log.TraceLog("PCIE_Device.DeviceValidate: Device does not have "
                + "any active memory or I/O address spaces " + "(" +
                this.ToString(false) + ")" );
            return true;
        } 

#endregion

#region " Device Open/Close " 
        /****************************
         *  Device Open & Close      *
         *****************************/

        /* public methods */

        public virtual DWORD Open()//pcie类方法
        {
            DWORD dwStatus;
            WD_PCI_CARD_INFO deviceInfo = new WD_PCI_CARD_INFO();//定义一个设备数据类
            //检索设备源信息
            /* Retrieve the device's resources information */
            deviceInfo.pciSlot = slot;
            dwStatus = wdc_lib_decl.WDC_PciGetDeviceInfo(deviceInfo);//调用API 获得状态信息
            if ((DWORD)wdc_err.WD_STATUS_SUCCESS != dwStatus)//如果不成功
            {
                Log.ErrLog("PCIE_Device.Open: Failed retrieving the " 
                    + "device's resources information. Error 0x" + //在log中打印数据
                    dwStatus.ToString("X") + ": " + utils.Stat2Str(dwStatus) +
                    "(" + this.ToString(false) +")" );
                return dwStatus;
             }

            /* NOTE: You can modify the device's resources information here, 
             * if necessary (mainly the deviceInfo.Card.Items array or the
             * items number - deviceInfo.Card.dwItems) in order to register
             * only some of the resources or register only a portion of a
             * specific address space, for example. 
             注意：如果需要，您可以在此处修改设备的资源信息（主要是deviceInfo.Card.Items数组或项目编号 - deviceInfo.Card.dwItems），
             以仅注册一些资源或仅注册特定部分 地址空间，例如。
             */

            dwStatus = wdc_lib_decl.WDC_PciDeviceOpen(ref m_wdcDevice,
                deviceInfo, IntPtr.Zero, IntPtr.Zero, "", IntPtr.Zero);//状态正常之后尝试打开

            if ((DWORD)wdc_err.WD_STATUS_SUCCESS != dwStatus)//如果打开失败
            {
                Log.ErrLog("PCIE_Device.Open: Failed opening a " +
                    "WDC device handle. Error 0x" + dwStatus.ToString("X") +
                    ": " + utils.Stat2Str(dwStatus) + "(" + 
                    this.ToString(false) + ")");
                goto Error;
            }

            Log.TraceLog("PCIE_Device.Open: Opened a PCI device " + 
                this.ToString(false));//更新 成功打开 调用tostring方法

            /* Validate device information *///验证信息 地址空间是否存在
            if (DeviceValidate() != true)
            {
                dwStatus = (DWORD)wdc_err.WD_NO_RESOURCES_ON_DEVICE;
                goto Error;
            }

            return dwStatus;
Error:    
            if (Handle != IntPtr.Zero)
                Close();

            return dwStatus;
        }

        public virtual bool Close()//关闭
        {
            DWORD dwStatus;
            //Handle是属性
            if (Handle == IntPtr.Zero)
            {
                Log.ErrLog("PCIE_Device.Close: Error - NULL " 
                    + "device handle");
                return false;
            }

            /* unregister events*///注销事件
            dwStatus = EventUnregister();

            /* Disable interrupts *///中断中断
            dwStatus = DisableInterrupts();

            /* Close the device *///关闭设备
            dwStatus = wdc_lib_decl.WDC_PciDeviceClose(Handle);
            if ((DWORD)wdc_err.WD_STATUS_SUCCESS != dwStatus)
            {
                Log.ErrLog("PCIE_Device.Close: Failed closing a "
                    + "WDC device handle (0x" + Handle.ToInt64().ToString("X") 
                    + ". Error 0x" + dwStatus.ToString("X") + ": " +
                    utils.Stat2Str(dwStatus) + this.ToString(false));
            }
            else
            {
                Log.TraceLog("PCIE_Device.Close: " +
                    this.ToString(false) + " was closed successfully");
            }

            return ((DWORD)wdc_err.WD_STATUS_SUCCESS == dwStatus);
        }

#endregion

#region " Interrupts "
            /* public methods */
        public bool IsEnabledInt()
        {
            return wdc_lib_decl.WDC_IntIsEnabled(this.Handle);
        }

        protected virtual DWORD CreateIntTransCmds(out WD_TRANSFER[] 
            pIntTransCmds, out DWORD dwNumCmds)
        {
            /* Define the number of interrupt transfer commands to use */
            DWORD NUM_TRANS_CMDS = 0;
            pIntTransCmds = new WD_TRANSFER[NUM_TRANS_CMDS];
            /*
            TODO: Your hardware has level sensitive interrupts, which must be
          acknowledged in the kernel immediately when they are received.
                  Since the information for acknowledging the interrupts is
            hardware-specific, YOU MUST ADD CODE to read/write the relevant
            register(s) in order to correctly acknowledge the interrupts
            on your device, as dictated by your hardware's specifications.
            When adding transfer commands, be sure to also modify the
            definition of NUM_TRANS_CMDS (above) accordingly.
             
            *************************************************************************   
            * NOTE: If you attempt to use this code without first modifying it in   *
            *       order to correctly acknowledge your device's interrupts, as     *
            *       explained above, the OS will HANG when an interrupt occurs!     *
            *************************************************************************
            */
            dwNumCmds = NUM_TRANS_CMDS;
            return (DWORD)wdc_err.WD_STATUS_SUCCESS;
        }

        protected virtual DWORD DisableCardInts()
        {
            /* TODO: You can add code here to write to the device in order
             * to physically disable the hardware interrupts */ 
            return (DWORD)wdc_err.WD_STATUS_SUCCESS;
        }

        protected BOOL IsItemExists(WDC_DEVICE Dev, DWORD item)
        {
            int i;
            DWORD dwNumItems = Dev.cardReg.Card.dwItems;

            for (i=0; i<dwNumItems; i++)
            {
                if (Dev.cardReg.Card.Item[i].item == item)
                    return true;
            }

            return false;
        }


        public DWORD EnableInterrupts(USER_INTERRUPT_CALLBACK userIntCb, IntPtr pData)
        {
            DWORD dwStatus;
            WD_TRANSFER[] pIntTransCmds = null;
            DWORD dwNumCmds;
            if(userIntCb == null)
            {
                Log.TraceLog("PCIE_Device.EnableInterrupts: "
                    + "user callback is invalid");
                return (DWORD)wdc_err.WD_INVALID_PARAMETER;
            }

            if(!IsItemExists(m_wdcDevice, (DWORD)item_types.ITEM_INTERRUPT))
            {
                Log.TraceLog("PCIE_Device.EnableInterrupts: "
                    + "Device doesn't have any interrupts");
                return (DWORD)wdc_err.WD_OPERATION_FAILED;
            }

            m_userIntHandler = userIntCb;

            m_intHandler = new INT_HANDLER(PCIE_IntHandler);
            if(m_intHandler == null)
            {
                Log.ErrLog("PCIE_Device.EnableInterrupts: interrupt handler is " +
                    "null (" + this.ToString(false) + ")" ); 
                return (DWORD)wdc_err.WD_INVALID_PARAMETER;
            }

            if(wdc_lib_decl.WDC_IntIsEnabled(Handle))
            {
                Log.ErrLog("PCIE_Device.EnableInterrupts: "
                    + "interrupts are already enabled (" +
                    this.ToString(false) + ")" );
                return (DWORD)wdc_err.WD_OPERATION_ALREADY_DONE;
            }

            dwStatus = CreateIntTransCmds(out pIntTransCmds, out dwNumCmds);
            if (dwStatus != (DWORD)wdc_err.WD_STATUS_SUCCESS)
                return dwStatus;
            dwStatus = wdc_lib_decl.WDC_IntEnable(wdcDevice, pIntTransCmds,
                dwNumCmds, 0, m_intHandler, pData, wdc_defs_macros.WDC_IS_KP(wdcDevice));

            if ((DWORD)wdc_err.WD_STATUS_SUCCESS != dwStatus)
            {
                Log.ErrLog("PCIE_Device.EnableInterrupts: Failed "
                    + "enabling interrupts. Error " + dwStatus.ToString("X") + ": " 
                    + utils.Stat2Str(dwStatus) + "(" + this.ToString(false) + ")");
                m_intHandler = null;
                return dwStatus;
            }
            /* TODO: You can add code here to write to the device in order
                 to physically enable the hardware interrupts */

            Log.TraceLog("PCIE_Device: enabled interrupts (" + this.ToString(false) + ")");
            return dwStatus;
        }

        public DWORD DisableInterrupts()
        {
            DWORD dwStatus;

            if (!wdc_lib_decl.WDC_IntIsEnabled(this.Handle))
            {
                Log.ErrLog("PCIE_Device.DisableInterrupts: interrupts are already disabled... " +
                    "(" + this.ToString(false) + ")" );
                return (DWORD)wdc_err.WD_OPERATION_ALREADY_DONE;
            }

            /* Physically disabling the hardware interrupts */
            dwStatus = DisableCardInts();

            dwStatus = wdc_lib_decl.WDC_IntDisable(m_wdcDevice);
            if (dwStatus != (DWORD)wdc_err.WD_STATUS_SUCCESS)
            {
                Log.ErrLog("PCIE_Device.DisableInterrupts: Failed to" +
                    "disable interrupts. Error " + dwStatus.ToString("X") 
                    + ": " + utils.Stat2Str(dwStatus) + " (" +
                    this.ToString(false) + ")" );
            }
            else
            {
                Log.TraceLog("PCIE_Device.DisableInterrupts: Interrupts are disabled" +
                    "(" + this.ToString(false) + ")");
            }

            return dwStatus;
        }

            /* private methods */
        private void PCIE_IntHandler(IntPtr pDev)
        {
            wdcDevice.Int =
                (WD_INTERRUPT)m_wdcDeviceMarshaler.MarshalDevWdInterrupt(pDev);

            /* to obtain the data that was read at interrupt use:
             * WD_TRANSFER[] transCommands;
             * transCommands = (WD_TRANSFER[])m_wdcDeviceMarshaler.MarshalDevpWdTrans(
             *     wdcDevice.Int.Cmd, wdcDevice.Int.dwCmds); */

            if(m_userIntHandler != null)
                m_userIntHandler(this);
        }

#endregion

#region " Events"
        /****************************
         *          Events          *
         * **************************/

        /* public methods */

        public bool IsEventRegistered()
        {
            if (Handle == IntPtr.Zero)
                return false;

            return wdc_lib_decl.WDC_EventIsRegistered(Handle);
        }

        public DWORD EventRegister(USER_EVENT_CALLBACK userEventHandler)
        {
            DWORD dwStatus;
            DWORD dwActions = (DWORD)windrvr_consts.WD_ACTIONS_ALL;
            /* TODO: Modify the above to set up the plug-and-play/power 
             * management events for which you wish to receive notifications.
             * dwActions can be set to any combination of the WD_EVENT_ACTION
             * flags defined in windrvr.h */

            if(userEventHandler == null)
            {
                Log.ErrLog("PCIE_Device.EventRegister: user callback is "
                    + "null");
                return (DWORD)wdc_err.WD_INVALID_PARAMETER;
            }

            /* Check if event is already registered */
            if(wdc_lib_decl.WDC_EventIsRegistered(Handle))
            {
                Log.ErrLog("PCIE_Device.EventRegister: Events are already "
                    + "registered ...");
                return (DWORD)wdc_err.WD_OPERATION_ALREADY_DONE;
            }

            m_userEventHandler = userEventHandler;

            /* Register event */
            dwStatus = wdc_lib_decl.WDC_EventRegister(m_wdcDevice, dwActions,
                m_eventHandler, Handle, wdc_defs_macros.WDC_IS_KP(wdcDevice));

            if ((DWORD)wdc_err.WD_STATUS_SUCCESS != dwStatus)
            {
                Log.ErrLog("PCIE_Device.EventRegister: Failed to register "
                    + "events. Error 0x" + dwStatus.ToString("X") 
                    + utils.Stat2Str(dwStatus));
                m_userEventHandler = null;
            }
            else
            {
                Log.TraceLog("PCIE_Device.EventRegister: events are " +
                    " registered (" + this.ToString(false) +")" );
            }

            return dwStatus;
        }

        public DWORD EventUnregister()
        {
            DWORD dwStatus;

            if (!wdc_lib_decl.WDC_EventIsRegistered(Handle))//检查设备是否被注册 参见文档
            {
                Log.ErrLog("PCIE_Device.EventUnregister: No events " +
                    "currently registered ...(" + this.ToString(false) + ")" );
                return (DWORD)wdc_err.WD_OPERATION_ALREADY_DONE;
            }

            dwStatus = wdc_lib_decl.WDC_EventUnregister(m_wdcDevice);//注销事件

            if ((DWORD)wdc_err.WD_STATUS_SUCCESS != dwStatus)
            {
                Log.ErrLog("PCIE_Device.EventUnregister: Failed to " +
                    "unregister events. Error 0x" + dwStatus.ToString("X") +
                    ": " + utils.Stat2Str(dwStatus) + "(" +
                    this.ToString(false) + ")");
            }
            else
            {
                Log.TraceLog("PCIE_Device.EventUnregister: Unregistered " +
                    " events (" + this.ToString(false) + ")" );
            }

            return dwStatus;
        }

        /** private methods **/

        /* event callback method */
        private void PCIE_EventHandler(IntPtr pWdEvent, IntPtr pDev)
        {
            MarshalWdEvent wdEventMarshaler = new MarshalWdEvent();
            WD_EVENT wdEvent = (WD_EVENT)wdEventMarshaler.MarshalNativeToManaged(pWdEvent);
            m_wdcDevice.Event =
                (WD_EVENT)m_wdcDeviceMarshaler.MarshalDevWdEvent(pDev);
            if(m_userEventHandler != null)
                m_userEventHandler(ref wdEvent, this); 
        }
#endregion


    }
}
