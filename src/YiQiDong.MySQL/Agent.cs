using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using YiQiDong.MySQL.Functions;
using YiQiDong.Core;
using YiQiDong.Core.Utils;
using YiQiDong.Protocol.V1.Model;

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

            AddFunction(new Config("配置修改",imageFolder, containerFolder),false);
            AddFunction(new Config("配置查看", imageFolder, containerFolder), true);

            AddFunction(new AdvancedConfig(containerFolder));
            AddFunction(new PasswordManager(imageFolder, containerFolder), true);
            AddFunction(new SqlQuery());

            //检查复制data目录
            FolderUtils.CopyFolder(Path.Combine(imageFolder, "data"), Path.Combine(containerFolder, "data"));
            //检查复制my.ini文件
            FolderUtils.CopyFile(Path.Combine(imageFolder, "my.ini"), containerFolder);
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
                    ConsoleOutputHandler?.Invoke($"启动容器时失败，原因：{ex}");
                }
            });
        }

        private bool IsRunAsRoot()
        {
            ConsoleOutputHandler?.Invoke("正在检查当前用户...");
            var tmpPsi = new ProcessStartInfo("whoami");
            tmpPsi.RedirectStandardOutput = true;
            var tmpProcess = Process.Start(tmpPsi);
            var account = tmpProcess.StandardOutput.ReadToEnd();
            return account.Trim() == "root";
        }

        private void outputNotSupportOsAndArchitecture()
        {
            ConsoleOutputHandler?.Invoke($"不支持的操作系统[{RuntimeInformation.OSDescription}]+平台架构[{RuntimeInformation.OSArchitecture}]。");
        }

        private void innnerStart()
        {
            if (Process != null)
                return;
            if (!ContainerInfo.AutoStart)
                return;

            var imageFolder = ImagePathUtils.GetImageFolder(ContainerInfo.ImageId);
            var containerFolder = ContainerPathUtils.GetContainerFolder(ContainerInfo.Id);

            var process_filename = "";
            var process_arguments = $"--defaults-file=\"{Path.Combine(containerFolder, "my.ini")}\" --datadir=\"{Path.Combine(containerFolder, "data")}\"";

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
                            process_arguments += $" --socket=\"{Path.Combine(containerFolder, "mysqld.sock")}\"";

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
                            process_arguments += $" --socket=\"{Path.Combine(containerFolder, "mysqld.sock")}\"";
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
            }
            else
            {
                outputNotSupportOsAndArchitecture();
                return;
            }
            ConsoleOutputHandler?.Invoke("Process Filename：" + process_filename);
            ConsoleOutputHandler?.Invoke("Process Arguments：" + process_arguments);

            ProcessStartInfo psi = new ProcessStartInfo(process_filename, process_arguments);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = containerFolder;

            Process = Process.Start(psi);
            Process.EnableRaisingEvents = true;
            Process.OutputDataReceived += Process_OutputDataReceived;
            Process.ErrorDataReceived += Process_ErrorDataReceived;
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
            ConsoleOutputHandler?.Invoke($"进程[Id:{Process.Id},Name:{Process.ProcessName}]已经启动。");
            Process.Exited += Process_Exited;
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            ConsoleOutputHandler?.Invoke(e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            ConsoleOutputHandler?.Invoke(e.Data);
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
            ConsoleOutputHandler?.Invoke($"进程[Id:{Process.Id},Name:{Process.ProcessName}]已经退出，退出码：{Process.ExitCode}。");
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

            host = Config.Instance.GetConnectHost();
            port = Config.Instance.GetConnectPort();
            user = "root";
            password = PasswordManager.Instance.Properties["password"];
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
