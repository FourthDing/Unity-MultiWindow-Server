using System;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace WindowServer.IO
{
    public enum PipedCmdType : sbyte
    {
        // C -> S
        ClientOkay = 0,
        NewWindow = 1,// 带字符串（UTF8,Log名）
        S2CPipe = 2,// 带字符串（UTF8,回传管道路径）
        SharedMem = 3,// 带字符串（UTF8,共享内存文件路径）
        SetGeometry = 4,// 带四个int（WindowGeometry）
        NewWindowTitle = 5,// 带字符串（UTF8,新标题）
        ClientEnd = 6,// 啥也不带,表示要润了

        // S -> C
        ServerOkay = -1,// 啥也不带
        ServerFailed = -2,
        UpdateGeometry = -3,// 带四个小端序int（Length==16）
        Echo = -4,// 带字符串（UTF8,回显文本（测试用））
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PipedCmdHeader
    {
        public PipedCmdType Type;
        public ulong RequestId;
        public byte Length;

        public PipedCmdHeader(PipedCmdType type, byte length = 0, ulong requestId = 0)
        {
            this.Type = type;
            this.RequestId = requestId;
            this.Length = length;
        }
    };
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WindowGeometry
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public WindowGeometry(int x,int y,int w,int h)
        {
            X = x;
            Y = y;
            Width = w;
            Height = h;
        }
    }
}