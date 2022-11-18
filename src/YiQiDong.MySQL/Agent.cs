using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using YiQiDong.Core;
using YiQiDong.Core.Utils;
using YiQiDong.Protocol.V1.Model;
using YiQiDong.Agent;
using Mono.Unix.Native;

namespace YiQiDong.MySQL
{
    public class Agent : AbstractAgent
    {
        public static Agent Instance { get; private set; }

        public Process Process { get; set; }

        public Agent()
        {
            Instance = this;
        }

        public override void Init(ContainerInfo contentT)
        {
            base.Init(contentT);

            var imageFolder = ImagePathUtils.GetImageFolder(ContainerInfo.ImageId);
            var containerFolder = ContainerPathUtils.GetContainerFolder(ContainerInfo.Id);

            AddFunction(new Functions.Config("配置修改",imageFolder, containerFolder),false);
            AddFunction(new Functions.Config("配置查看", imageFolder, containerFolder), true);

            AddFunction(new Functions.PasswordManager(), true);
            AddFunction(new Functions.SqlQuery());
        }

        public override void Start()
        {
            base.Start();
            Task.Run(() =>
            {
                try
                {
                    innnerStart();
                }
                catch (Exception ex)
                {
                    AgentContext.Instance.LogError($"启动容器时失败，原因：{ex}");
                }
            });
        }

        private bool IsRunAsRoot()
        {
            AgentContext.Instance.LogInfo("正在检查当前用户...");
            var tmpPsi = new ProcessStartInfo("whoami");
            tmpPsi.RedirectStandardOutput = true;
            var tmpProcess = Process.Start(tmpPsi);
            var account = tmpProcess.StandardOutput.ReadToEnd();
            return account.Trim() == "root";
        }

        private void outputNotSupportOsAndArchitecture()
        {
            AgentContext.Instance.LogWarn($"不支持的操作系统[{RuntimeInformation.OSDescription}]+平台架构[{RuntimeInformation.OSArchitecture}]。");
        }

        private void innnerStart()
        {
            if (Process != null)
                return;
            if (!ContainerInfo.AutoStart)
                return;

            var imageFolder = ImagePathUtils.GetImageFolder(ContainerInfo.ImageId);
            var dataFolder = Functions.Config.Instance.GetDataFolder();

            //检查复制data目录
            FolderUtils.CopyFolder(Path.Combine(imageFolder, "data"), Path.Combine(dataFolder, "data"));
            //检查复制my.ini文件
            FolderUtils.CopyFile(Path.Combine(imageFolder, "my.ini"), dataFolder);

            var process_filename = "";
            var process_arguments = $"--defaults-file=\"{Path.Combine(dataFolder, "my.ini")}\" --datadir=\"{Path.Combine(dataFolder, "data")}\"";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                switch (RuntimeInformation.OSArchitecture)
                {
                    case Architecture.X64:
                        process_filename = Path.Combine(imageFolder, "mysql-win_x64", "bin", "mysqld.exe");
                        process_arguments += " --console";
                        break;
                    default:
                        outputNotSupportOsAndArchitecture();
                        return;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                switch (RuntimeInformation.OSArchitecture)
                {
                    case Architecture.X64:
                        {
                            process_filename = Path.Combine(imageFolder, "mysql-linux_x64", "bin", "mysqld");
                            if (IsRunAsRoot())
                                process_arguments += " --user=root";
                            process_arguments += $" --basedir=\"{Path.Combine(imageFolder, "mysql-linux_x64")}\"";
                            process_arguments += $" --socket=\"{Path.Combine(dataFolder, "mysqld.sock")}\"";

                            //检查文件
                            FolderUtils.CopyFile(Path.Combine(imageFolder, "mysql-linux_x64", "lib", "libaio.so.1"), "/usr/lib/x86_64-linux-gnu");
                            FolderUtils.CopyFile(Path.Combine(imageFolder, "mysql-linux_x64", "lib", "libnuma.so.1"), "/usr/lib/x86_64-linux-gnu");
                            break;
                        }
                    case Architecture.Arm:
                        {
                            process_filename = Path.Combine(imageFolder, "mysql-linux_arm", "bin", "mysqld");
                            if (IsRunAsRoot())
                                process_arguments += " --user=root";
                            process_arguments += $" --basedir=\"{Path.Combine(imageFolder, "mysql-linux_arm")}\"";
                            process_arguments += $" --socket=\"{Path.Combine(dataFolder, "mysqld.sock")}\"";
                            process_arguments += " --secure-file-priv=\"\"";
                            process_arguments += " --console";

                            //检查文件
                            FolderUtils.CopyFile(Path.Combine(imageFolder, "mysql-linux_arm", "lib", "libaio.so.1"), "/usr/lib/arm-linux-gnueabihf");
                            FolderUtils.CopyFile(Path.Combine(imageFolder, "mysql-linux_arm", "lib", "libwrap.so.0"), "/usr/lib/arm-linux-gnueabihf");
                            break;
                        }
                    default:
                        outputNotSupportOsAndArchitecture();
                        return;
                }
                //为进程添加可执行权限
                Syscall.chmod(process_filename, FilePermissions.S_IRWXU | FilePermissions.S_IRGRP | FilePermissions.S_IXGRP | FilePermissions.S_IROTH | FilePermissions.S_IXOTH);
            }
            else
            {
                outputNotSupportOsAndArchitecture();
                return;
            }
            AgentContext.Instance.LogInfo("Process Filename：" + process_filename);
            AgentContext.Instance.LogInfo("Process Arguments：" + process_arguments);

            ProcessStartInfo psi = new ProcessStartInfo(process_filename, process_arguments);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = dataFolder;

            Process = Process.Start(psi);
            Process.EnableRaisingEvents = true;
            Process.OutputDataReceived += Process_OutputDataReceived;
            Process.ErrorDataReceived += Process_ErrorDataReceived;
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
            AgentContext.Instance.LogInfo($"进程[Id:{Process.Id},Name:{Process.ProcessName}]已经启动。");
            Process.Exited += Process_Exited;
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            AgentContext.Instance.LogInfo(e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            AgentContext.Instance.LogInfo(e.Data);
        }

        private void delayStart()
        {
            Task.Delay(5000).ContinueWith(t =>
            {
                innnerStart();
            });
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            AgentContext.Instance.LogInfo($"进程[Id:{Process.Id},Name:{Process.ProcessName}]已经退出，退出码：{Process.ExitCode}。");
            Process = null;
            delayStart();
        }

        public override void Stop()
        {
            //发送shutdown
            string host;
            int port;
            string user;
            string password;

            host = Functions.Config.Instance.GetConnectHost();
            port = Functions.Config.Instance.GetConnectPort();
            user = "root";
            password = Functions.Config.Instance.GetPassword();
            try
            {
                var connectionString = $"Server={host};Port={port};Database=mysql;Uid={user};Pwd={password};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand("shutdown;", connection))
                        cmd.ExecuteNonQuery();
                    connection.Close();
                }
                //等待30秒
                Process.WaitForExit(30 * 1000);
            }
            catch { }
            if (Process == null
                || Process.HasExited)
                return;
            ProcessUtils.KillProcessTree(Process);
            base.Stop();
        }
    }
}
