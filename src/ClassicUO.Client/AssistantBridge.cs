using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClassicUO
{
    public static class AssistantBridge
    {
        [UnmanagedCallersOnly(EntryPoint = "AssistantBridge_SendMapMessage", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int SendMapMessageExport(IntPtr message)
        {
            return SendMapMessage(Marshal.PtrToStringUni(message)) ? 1 : 0;
        }

        public static bool SendMapMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            Client.Game?.UO?.World?.UoAssist?.SignalMessage(message);

            return Client.Game?.UO?.World?.UoAssist != null;
        }
    }
}
