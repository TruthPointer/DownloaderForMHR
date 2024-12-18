using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Diagnostics;
using System.IO;

namespace DownloaderForMHR
{
    class CmdHelper
    {
        private static string CmdPath = @"C:\Windows\System32\cmd.exe";

        /// <summary>
        /// 执行cmd命令
        /// 多命令请使用批处理命令连接符：
        /// <![CDATA[
        /// &:同时执行两个命令
        /// |:将上一个命令的输出,作为下一个命令的输入
        /// &&：当&&前的命令成功时,才执行&&后的命令
        /// ||：当||前的命令失败时,才执行||后的命令]]>
        /// 其他请自行搜索
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="output"></param>
        public static void RunCmd(string cmd, out string output)
        {
            cmd = cmd.Trim().TrimEnd('&') + "&exit";//说明：不管命令是否成功均执行exit命令，否则当调用ReadToEnd()方法时，会处于假死状态
            using (Process p = new Process())
            {
                p.StartInfo.FileName = CmdPath;
                p.StartInfo.UseShellExecute = false;        //是否使用操作系统shell启动
                p.StartInfo.RedirectStandardInput = true;   //接受来自调用程序的输入信息
                p.StartInfo.RedirectStandardOutput = true;  //由调用程序获取输出信息
                p.StartInfo.RedirectStandardError = true;   //重定向标准错误输出
                p.StartInfo.CreateNoWindow = true;          //不显示程序窗口
                p.Start();//启动程序

                //向cmd窗口写入命令
                p.StandardInput.WriteLine(cmd);
                p.StandardInput.AutoFlush = true;

                //获取cmd窗口的输出信息
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();//等待程序执行完退出进程
                p.Close();
            }
        }

        public static void RunCmdAndGetResult(Process p, string cmd, string arguments, Boolean needExitArgument)
        {
            if (p == null)
                p = new Process();
            p.StartInfo.FileName = cmd;
            p.StartInfo.Arguments = arguments + (needExitArgument ? (arguments.Equals("") ? "" : "\nexit") : "");
            p.StartInfo.CreateNoWindow = true;         // 不创建新窗口    
            p.StartInfo.UseShellExecute = false;       //不启用shell启动进程  
            p.StartInfo.RedirectStandardInput = true;  // 重定向输入    
            p.StartInfo.RedirectStandardOutput = true; // 重定向标准输出    
            p.StartInfo.RedirectStandardError = true;  // 重定向错误输出  
            p.Start();
        }

        public static void RunCmdAndGetResult(Process p, string cmd, string arguments, out StreamReader reader)
        {
            if (p == null)
                p = new Process();
            p.StartInfo.FileName = cmd;
            p.StartInfo.Arguments = arguments;// +(arguments.Equals("")? "":"\n")+"exit";
            p.StartInfo.CreateNoWindow = true;         // 不创建新窗口    
            p.StartInfo.UseShellExecute = false;       //不启用shell启动进程  
            p.StartInfo.RedirectStandardInput = true;  // 重定向输入    
            p.StartInfo.RedirectStandardOutput = true; // 重定向标准输出    
            p.StartInfo.RedirectStandardError = true;  // 重定向错误输出  
            p.Start();
            reader = p.StandardOutput;//获取返回值 
        }

        public static void closeProcess(Process p)
        {
            p.WaitForExit();

            //判断程序是退出了进程 退出为true(上面的退出方法执行完后，HasExited的返回值为 true) 
            bool falg = p.HasExited;
            p.Close();
        }

        public static void RunCmdAndGetResultFromStandardOutput(string cmd, string arguments, out string output)
        {
            Process p = new Process();
            p.StartInfo.FileName = cmd;
            p.StartInfo.Arguments = arguments;
            p.StartInfo.CreateNoWindow = true;         // 不创建新窗口    
            p.StartInfo.UseShellExecute = false;       //不启用shell启动进程  
            p.StartInfo.RedirectStandardInput = true;  // 重定向输入    
            p.StartInfo.RedirectStandardOutput = true; // 重定向标准输出    
            p.StartInfo.RedirectStandardError = true;  // 重定向错误输出  
            p.Start();
            StreamReader sr = p.StandardOutput;//获取返回值 
            string? line;
            int num = 1;
            while ((line = sr.ReadLine()) != null)
            {
                if (line != "")
                {
                    Debug.WriteLine(line + " " + num++);
                }
            }
            output = p.StandardError.ReadToEnd();
        }

    }

}
