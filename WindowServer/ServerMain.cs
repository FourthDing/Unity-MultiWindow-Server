using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WindowServer;

Console.WriteLine("外挂式多窗口管理器");
Console.WriteLine("版本 .1");


// 窗口列表,用于cli追踪
Dictionary<uint, ExternalWindow> externalWindows = [];
uint num = 0;

// 主命名管道实例,负责把对面的新连接转接到一个新建的窗口
while (true)
{
    NamedPipeServerStream serverPipe = new("UnityMultiWindowServicePipe", PipeDirection.InOut, -1);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("创建了新的管道:"+num);
    Console.WriteLine("主程序:"+num+"等待管道响应...");
    serverPipe.WaitForConnection();
    serverPipe.Flush();
    Console.WriteLine("主程序:"+num+"连接状态变动!喜大普奔");
    Console.WriteLine("主程序:"+num+"正建立连接……");
    externalWindows.Add(num, new(serverPipe));
    num++;
}