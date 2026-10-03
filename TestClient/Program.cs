using StbImageSharp;
using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using WindowServer.IO;

Program program = new();
internal partial class Program
{
    NamedPipeServerStream recv;
    NamedPipeClientStream send = new("UnityMultiWindowServicePipe");
    byte[] HeaderBuffer = new byte[Unsafe.SizeOf<PipedCmdHeader>()];
    PipedCmdHeader Header => MemoryMarshal.Read<PipedCmdHeader>(HeaderBuffer);
    byte[] ContentBuffer = new byte[256];
    MemoryMappedFile sharedFB;

    public Program()
    {
        string appId;

        string[] args = Environment.GetCommandLineArgs();
        if(args.Length < 2)
        {
            appId = "TestApp-1";
        }
        else
        {
            appId = args[1];

        }
        recv = new(appId);
        sharedFB = MemoryMappedFile.CreateNew(appId+".fb",1/*这可是信号值*/+16869120);

        Console.WriteLine("创建了连接实例。等待服务端……");
        send.Connect();
        send.Flush();
        Console.WriteLine("服务端连接建立");
        Console.WriteLine("发送初通信……");
        PipeSendStr(PipedCmdType.S2CPipe, appId);
        Console.WriteLine("发送了初通信,等待服务器回连recv……");
        recv.WaitForConnection();
        Console.WriteLine("服务器回连了recv!");
        PipeSendStr(PipedCmdType.NewWindow,appId);
        Console.WriteLine("发送了NewWindow");
        PipeSend(PipedCmdType.SetGeometry,
            //new WindowGeometry(114,514,240,320)
            new WindowGeometry(114,514,324,300)
            );
        Console.WriteLine("发送了SetGeometry");

        byte[] result = ImageResult.FromMemory(File.ReadAllBytes("sequence/001.png"), ColorComponents.RedGreenBlueAlpha).Data;
        MemoryMappedViewAccessor accessor = sharedFB.CreateViewAccessor();
        byte writing = 1;
        byte okay = 0;
        accessor.Write(0,ref writing);
        accessor.WriteArray(1, result, 0, result.Length);
        accessor.Write(0, ref okay);
        //accessor.WriteArray<byte>(1910 * 1000,[21,54,76,43,76,23,76,24,86,10,54,36,96,16,46,34],0,16);
        //byte[] fbContent = [21, 54, 76, 43, 76, 23, 76, 24, 86, 10, 54, 36, 96, 16, 46, 34];
        //Console.WriteLine(BitConverter.ToString(fbContent));

        PipeSendStr(PipedCmdType.SharedMem, appId + ".fb");
        Console.WriteLine("发送了第一个共享内存路径");

        var dirEnum = Directory.EnumerateFiles("./sequence");
        List<string> things = new();
        foreach (var item in dirEnum)
        {
            things.Add(item);
        }
        int time = 0;
        while (time<10)
        {
            foreach (var item in things)
            {
                result = ImageResult.FromMemory(File.ReadAllBytes(item), ColorComponents.RedGreenBlueAlpha).Data;
                accessor.Write(0, ref writing);
                accessor.WriteArray(1, result, 0, result.Length);
                accessor.Write(0, ref okay);
                Thread.Sleep(1000/60);
            }
            time++;
            Console.WriteLine(time.ToString());
        }
        PipeSend<int>(PipedCmdType.ClientEnd, 114514);
        recv.Disconnect();
        send.Dispose();
        recv.Dispose();
        accessor.Dispose();
        sharedFB.Dispose();

    }
    public void PipeSend<Type>(PipedCmdType headerType, in Type content) where Type:unmanaged
    {
        MemoryMarshal.Write<Type>(ContentBuffer,content);
        MemoryMarshal.Write(HeaderBuffer, new PipedCmdHeader(headerType, Convert.ToByte(Unsafe.SizeOf<Type>())));
        send.Write(HeaderBuffer);
        send.Write(ContentBuffer.AsSpan(0,Unsafe.SizeOf<Type>()));
    }
    public void PipeSendStr(PipedCmdType headerType, string content)
    {
        int length = Encoding.UTF8.GetBytes(content, ContentBuffer.AsSpan<byte>());
        MemoryMarshal.Write(HeaderBuffer, new PipedCmdHeader(headerType, Convert.ToByte(length)));
        send.Write(HeaderBuffer);
        send.Write(ContentBuffer.AsSpan(0,length));
    }
    public async Task PipeRecv()
    {
        await Task.WhenAll();
    }
}
