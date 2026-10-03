using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowServer.IO;

namespace WindowServer
{
    internal class ExternalWindow :IDisposable
    {
        private Thread windowHandler;
        private Thread pipeHandler;
        private CancellationTokenSource pipeThreadCTS =new();
        public String LogName;
        private readonly WindowOptions windowOptions;
        private readonly IWindow window;
        private GL gl;
        Color col = Color.White;
        NamedPipeServerStream recv;
        NamedPipeClientStream send;
        private byte[] HeaderBuffer = new byte[Unsafe.SizeOf<PipedCmdHeader>()];
        PipedCmdHeader Header => MemoryMarshal.Read<PipedCmdHeader>(HeaderBuffer);
        byte[] ContentBuffer = new byte[256];
        MemoryMappedFile sharedFB;
        MemoryMappedViewAccessor sharedFBAccessor;
        //IntPtr sharedFBpointer;
        public ExternalWindow(NamedPipeServerStream recvPipe, WindowOptions? windowOptionsOverride = null)
        {
            recv = recvPipe;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("_新窗口:等待初通信……");



            //接收 S->C 命名管道连接
            Console.WriteLine("_新窗口:接受初连接……");
            recv.ReadExactly(HeaderBuffer, 0, Unsafe.SizeOf<PipedCmdHeader>());
            recv.ReadExactly(ContentBuffer, 0, Header.Length);
            if (Header.Type == PipedCmdType.S2CPipe && Header.Length != 0)
            {
                Console.WriteLine("_新窗口:获得合法S2C管道路径:" + Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length) + "。回连中……");
                send = new(Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length));
                send.Connect();
            }
            else
            {
                Console.WriteLine("???毛病?");
                throw new NotImplementedException("回传管道建立失败:初通信异常");
            }
            //检验回传连接
            MemoryMarshal.Write<PipedCmdHeader>(HeaderBuffer, new PipedCmdHeader(PipedCmdType.ServerOkay));
            send.WriteAsync(HeaderBuffer);
            foreach (var item in HeaderBuffer)
            {
                Console.Write(item + " ");
            }
            Console.WriteLine();


            //接收NewWindow
            Console.WriteLine("_新窗口:等待NewWindow……");
            recv.ReadExactly(HeaderBuffer, 0, Unsafe.SizeOf<PipedCmdHeader>());
            foreach (var item in HeaderBuffer)
            {
                Console.Write(item + " ");
            }
            Console.WriteLine();
            recv.ReadExactly(ContentBuffer, 0, Header.Length);
            if (Header.Type == PipedCmdType.NewWindow && Header.Length != 0)
            {
                LogName = Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length);
                Console.WriteLine(LogName + ":获得窗口Log名。");
            }
            else
            {
                foreach (var item in HeaderBuffer)
                {
                    Console.Write(item + " ");
                }
                Console.WriteLine();
                Console.WriteLine("???毛病?");
                throw new NotImplementedException("回传管道建立失败:初通信异常");
            }



            //接收首个SetGeometry，启动新窗口
            Console.WriteLine(LogName + ":等待SetGeometry……");
            recv.ReadExactly(HeaderBuffer, 0, Unsafe.SizeOf<PipedCmdHeader>());
            recv.ReadExactly(ContentBuffer, 0, Header.Length);
            WindowGeometry geometry = MemoryMarshal.Read<WindowGeometry>(ContentBuffer);
            if (Header.Type == PipedCmdType.SetGeometry && Header.Length == Unsafe.SizeOf<WindowGeometry>())
            {
                Console.WriteLine(LogName + ":获得Geometry:" +
                    geometry.Height + "x" + geometry.Width + "+" + geometry.X + "x" + geometry.Y);
            }
            else
            {
                Console.WriteLine("???毛病?");
                throw new NotImplementedException("回传管道建立失败:初通信异常");
            }

            //接收首个SharedMem建立共享内存
            Console.WriteLine(LogName + ":等待SharedMem……");
            recv.ReadExactly(HeaderBuffer, 0, Unsafe.SizeOf<PipedCmdHeader>());
            recv.ReadExactly(ContentBuffer, 0, Header.Length);
            if (Header.Type == PipedCmdType.SharedMem && Header.Length != 0)
            {
                Console.WriteLine(LogName + ":获得共享内存路径:" + Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length));
                //好家伙，跨平台有差异啊（还没试过）
                if (OperatingSystem.IsLinux())
                {
                    sharedFB = MemoryMappedFile.CreateFromFile("/tmp/UnityMultiwindowServer/" + Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length));
                }
                else if (OperatingSystem.IsMacOS())
                {
                    sharedFB = MemoryMappedFile.CreateFromFile("/shm_" + Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length));
                }
                else if (OperatingSystem.IsWindows())
                {
                    sharedFB = MemoryMappedFile.OpenExisting(Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length));
                    //byte[] fbContent = new byte[16];
                    //MemoryMappedViewAccessor sharedFBAccessor = sharedFB.CreateViewAccessor();
                    //sharedFBAccessor.ReadArray<byte>(1910*1000,fbContent,0,16);
                    //Console.WriteLine(BitConverter.ToString(fbContent));
                }
                else throw new NotImplementedException("Nooo我真真没考虑你平台咋共享内存啊啊");
                sharedFBAccessor = sharedFB.CreateViewAccessor();
                //危危险险地拿了指针(不过最后还是没使就是了……)
                //sharedFBpointer = sharedFBAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
            }
            else
            {
                Console.WriteLine("???毛病?");
                throw new NotImplementedException("回传管道建立失败:初通信异常");
            }


            //设置窗口
            windowOptions = windowOptionsOverride ?? WindowOptions.Default;
            windowOptions = windowOptions with
            {
                API = new GraphicsAPI(ContextAPI.OpenGL, new APIVersion(4, 6)),
                Size = new Silk.NET.Maths.Vector2D<int>(geometry.Width,geometry.Height),
                Position = new(geometry.X, geometry.Y),
            };
            window = Window.Create(windowOptions);

            //新开线程进入循环
            windowHandler = new(new ThreadStart(HandleWindow));
            windowHandler.Start();
            pipeHandler = new(new ThreadStart(HandlePipe));
            pipeHandler.Start();
            Console.ForegroundColor = ConsoleColor.White;

        }

        public void Dispose()
        {
            window.Close();
            //已包含gl.Dispose();
            pipeThreadCTS.Cancel();
            recv.Disconnect();
            send.Dispose();
            recv.Dispose();
            sharedFBAccessor.Dispose();
            sharedFB.Dispose();
            Console.WriteLine(LogName + "似了。");
        }
        void HandleWindow()
        {
            //照着silk开始剋GL

            //GL要画一个三角形，需要：
            //
            uint vertexShader = 0;
            uint fragmentShader = 0;
            uint shaderProg = 0;
            uint vbo = 0;
            uint vao = 0;
            uint ebo = 0;
            uint sharedFBTexture = 0;
            const string vertexCode = @"
#version 460 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTexCoords;
out vec2 frag_texCoords;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
    frag_texCoords = aTexCoords;
}";
            const string fragmentCode = @"
#version 460 core
in vec2 frag_texCoords;
uniform sampler2D uTexture;
out vec4 out_color;

void main()
{
    //out_color = vec4(frag_texCoords.x, frag_texCoords.y, 0.0, 1.0);
    out_color = texture(uTexture, frag_texCoords);
}";
            


            Console.WriteLine(LogName + ":新线程出生啦！正启动新窗口……");
            window.Load += () =>
            {
                gl = window.CreateOpenGL();

                //缓冲区
                vao = gl.GenVertexArray();
                gl.BindVertexArray(vao);
                vbo = gl.GenBuffer();
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
                ebo = gl.GenBuffer();
                gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);

                //灌顶点
                float[] vertices =
                {
                    -1f,  1f, 1f,  0.0f, 1.0f,//↖️
                    -1f, -1f, 1f,  0.0f, 0.0f,//↙️
                     1f, -1f, 1f,  1.0f, 0.0f,//↘️
                     1f,  1f, 1f,  1.0f, 1.0f,//↗️
                };
                gl.BufferData(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);
                //顶点的着色器参数
                gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 
                    5 * sizeof(float),//一段顶点数据的全长(必须全一致)
                    0);
                gl.EnableVertexAttribArray(0);

                //纹理
                sharedFBTexture = gl.GenTexture();
                gl.ActiveTexture(TextureUnit.Texture0);
                gl.BindTexture(TextureTarget.Texture2D, sharedFBTexture);
                gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false,
                    5 * sizeof(float),
                    3 * sizeof(float)//在顶点位置的参数后面，偏移3个float
                    );
                gl.EnableVertexAttribArray(1);

                //着色器
                vertexShader = gl.CreateShader(ShaderType.VertexShader);
                gl.ShaderSource(vertexShader, vertexCode);
                gl.CompileShader(vertexShader);
                fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
                gl.ShaderSource(fragmentShader, fragmentCode);
                gl.CompileShader(fragmentShader);
                shaderProg = gl.CreateProgram();
                gl.AttachShader(shaderProg, vertexShader);
                gl.AttachShader(shaderProg, fragmentShader);
                gl.LinkProgram(shaderProg);
                

                gl.GetProgram(shaderProg, ProgramPropertyARB.LinkStatus, out int lStatus);
                if (lStatus != (int)GLEnum.True)
                    throw new Exception("Program failed to link: " + gl.GetProgramInfoLog(shaderProg));

                
                

                //启动！！！
                gl.ClearColor(Color.OrangeRed);
                gl.Viewport(window.FramebufferSize);
            };
            window.Update += deltaSeconds =>
            {
                //if (deltaSeconds > 1.0/60.0)
                //{
                //    Console.ForegroundColor = ConsoleColor.Yellow;
                //    Console.WriteLine("警告！刷新率低于60："+deltaSeconds+ ">0.016666……");
                //    Console.ForegroundColor = ConsoleColor.White;
                //}
                //Console.WriteLine(deltaSeconds);
                //我知道这低效，而且没限制纹理大小
                if (sharedFBAccessor.ReadByte(0) == 0) {
                    byte[] texture = new byte[window.Size.X*window.Size.Y*4*sizeof(byte)];
                    sharedFBAccessor.ReadArray<byte>(1,texture,0,texture.Length);
                    gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                        ((uint)window.Size.X), ((uint)window.Size.Y), 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte,/*...*/ texture);
                    gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                    gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                    gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
                    gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
                    gl.BindTexture(TextureTarget.Texture2D, 0);
                }
            };
            window.Render += deltaSeconds =>
            {
                //先清
                gl.ClearColor(col);
                gl.Clear(ClearBufferMask.ColorBufferBit);

                //我也不知道为什么要再绑vao和use着色器，但示例这么用了
                gl.BindVertexArray(vao);
                gl.UseProgram(shaderProg);
                gl.DrawArrays(PrimitiveType.TriangleFan, 0, 4);//最后画三角形
            };
            window.FramebufferResize += newSize =>
            {
                Console.WriteLine(LogName + ":更新Geometry:" + newSize.ToString()+"+"+window.Position.ToString());
                gl.Viewport(newSize);
            };
            window.Closing += () =>
            {

            };
            //线程要管：
            //  更新窗口
            //  异步维护命名管道：
            window.Run();
            gl.Dispose();
        }
        void HandlePipe()
        {
            var token = pipeThreadCTS.Token;
            while (!token.IsCancellationRequested)
            {
                //读
                try
                {
                    recv.ReadExactly(HeaderBuffer, 0, Unsafe.SizeOf<PipedCmdHeader>());
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"{LogName}: 客户端直接断了: {ex.Message} 自Dispose得了");
                    Dispose();
                    return;
                }
                if (Header.Length >0) { recv.ReadExactly(ContentBuffer, 0, Header.Length); }
                switch (Header.Type)
                {
                    case PipedCmdType.ClientOkay:

                        break;
                    case PipedCmdType.NewWindow:
                        
                        break;
                    case PipedCmdType.S2CPipe:

                        break;
                    case PipedCmdType.SharedMem:
                        //更换共享内存
                        string newSharedMemName = Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length);
                        if (OperatingSystem.IsLinux())
                        {
                            sharedFB = MemoryMappedFile.CreateFromFile("/tmp/UnityMultiwindowServer/" + Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length));
                        }
                        else if (OperatingSystem.IsMacOS())
                        {
                            sharedFB = MemoryMappedFile.CreateFromFile("/shm_" + Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length));
                        }
                        else if (OperatingSystem.IsWindows())
                        {
                            sharedFB = MemoryMappedFile.OpenExisting(Encoding.UTF8.GetString(ContentBuffer, 0, Header.Length));
                        }
                        else throw new NotImplementedException("Nooo我真真没考虑你平台咋共享内存啊啊");
                        sharedFBAccessor = sharedFB.CreateViewAccessor();
                        //sharedFBpointer = sharedFBAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
                        
                        break;
                    case PipedCmdType.SetGeometry:
                        //重设geometry
                        WindowGeometry newGeometry = MemoryMarshal.Read<WindowGeometry>(ContentBuffer);
                        window.Position = new Vector2D<int>(newGeometry.Width,newGeometry.Height);
                        window.Size = new Vector2D<int>(newGeometry.X,newGeometry.Y);
                        break;
                    case PipedCmdType.NewWindowTitle:
                        //重设标题
                        window.Title = Encoding.UTF8.GetString(ContentBuffer,0,Header.Length);
                        break;
                    case PipedCmdType.ClientEnd:
                        Console.WriteLine(LogName + "后台要润了。");
                        Dispose();
                        break;
                    default:

                        break;
                }
            }
        }
    }
}


//勉强能用✅