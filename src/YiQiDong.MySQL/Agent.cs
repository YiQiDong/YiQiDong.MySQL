using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using YiQiDong.Core;
using YiQiDong.Core.Utils;
using YiQiDong.Agent;
using Mono.Unix.Native;
using MySqlConnector;

namespace YiQiDong.MySQL
{
    public class Agent : AbstractAgent
    {
        private string mysqlAppDir;
        private ProcessStartInfo psi;

        public static Agent Instance { get; private set; }

        public Process Process { get; set; }

        public Agent()
        {
            Instance = this;
        }

        public override void Init()
        {
            base.Init();
            if (AgentContext.IsContainerRuning)
            {
                var imageFolder = AgentContext.Container.ImageFolder;
                var containerFolder = AgentContext.Container.ContainerFolder;

                AddFunction(new Functions.Config("配置修改", imageFolder, containerFolder), false);
                AddFunction(new Functions.Config("配置查看", imageFolder, containerFolder), true);

                AddFunction(new Functions.PasswordManager(), true);
                AddFunction(new Functions.SqlQuery());

                var dataFolder = Functions.Config.Instance.GetDataFolder();
                var process_filename = "";
                var process_arguments = $"--defaults-file=\"{Path.Combine(dataFolder, "my.ini")}\" --datadir=\"{Path.Combine(dataFolder, "data")}\"";
                if (OperatingSystem.IsWindows())
                {
                    switch (RuntimeInformation.OSArchitecture)
                    {
                        case Architecture.X64:
                            mysqlAppDir = Path.Combine(imageFolder, "mysql-win-x64");
                            break;
                        default:
                            outputNotSupportOsAndArchitecture();
                            break;
                    }
                    process_filename = Path.Combine(mysqlAppDir, "bin", "mysqld.exe");
                    process_arguments += " --console";
                }
                else if (OperatingSystem.IsLinux())
                {
                    switch (RuntimeInformation.OSArchitecture)
                    {
                        case Architecture.X64:
                            mysqlAppDir = Path.Combine(imageFolder, "mysql-linux-x64");
                            break;
                        case Architecture.Arm64:
                            mysqlAppDir = Path.Combine(imageFolder, "mysql-linux-arm64");
                            break;
                        default:
                            outputNotSupportOsAndArchitecture();
                            break;
                    }
                    process_filename = Path.Combine(mysqlAppDir, "bin", "mysqld");
                    //为进程添加可执行权限
                    Syscall.chmod(process_filename, FilePermissions.S_IRWXU | FilePermissions.S_IRGRP | FilePermissions.S_IXGRP | FilePermissions.S_IROTH | FilePermissions.S_IXOTH);

                    if (IsRunAsRoot())
                        process_arguments += " --user=root";
                    process_arguments += $" --basedir=\"{mysqlAppDir}\"";
                    process_arguments += $" --socket=\"{Path.Combine(dataFolder, "mysqld.sock")}\"";
                    process_arguments += " --secure-file-priv=\"\"";
                    process_arguments += " --console";

                    var mysqlLibDir = Path.Combine(mysqlAppDir, "lib");
                    //添加PATH环境变量
                    var path = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
                    if (string.IsNullOrEmpty(path))
                        path = mysqlLibDir;
                    else
                        path = $"{path}:{mysqlLibDir}";
                    Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", path);
                }
                else
                {
                    outputNotSupportOsAndArchitecture();
                }
                psi = new ProcessStartInfo(process_filename, process_arguments);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;
                psi.UseShellExecute = false;
                psi.WorkingDirectory = dataFolder;
            }
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
                    AgentContext.LogError($"启动容器时失败，原因：{ex}");
                }
            });
        }

        private bool IsRunAsRoot()
        {
            AgentContext.LogInfo("正在检查当前用户...");
            var tmpPsi = new ProcessStartInfo("whoami");
            tmpPsi.RedirectStandardOutput = true;
            var tmpProcess = Process.Start(tmpPsi);
            var account = tmpProcess.StandardOutput.ReadToEnd();
            return account.Trim() == "root";
        }

        private void outputNotSupportOsAndArchitecture()
        {
            AgentContext.LogWarn($"不支持的操作系统[{RuntimeInformation.OSDescription}]+平台架构[{RuntimeInformation.OSArchitecture}]。");
        }

        private void innnerStart()
        {
            if (Process != null)
                return;
            if (!AgentContext.Container.AutoStart)
                return;

            var imageFolder = AgentContext.Container.ImageFolder;
            var dataFolder = Functions.Config.Instance.GetDataFolder();

            //检查复制data目录
            FilsSystemUtils.CopyFolder(Path.Combine(imageFolder, "data"), Path.Combine(dataFolder, "data"));
            //检查复制my.ini文件
            FilsSystemUtils.CopyFile(Path.Combine(imageFolder, "my.ini"), dataFolder);

            Process = Process.Start(psi);
            Process.EnableRaisingEvents = true;
            Process.OutputDataReceived += Process_OutputDataReceived;
            Process.ErrorDataReceived += Process_ErrorDataReceived;
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
            AgentContext.LogInfo($"进程[Id:{Process.Id},Name:{Process.ProcessName}]已经启动。");
            Process.Exited += Process_Exited;
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            AgentContext.LogInfo(e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            AgentContext.LogInfo(e.Data);
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
            AgentContext.LogInfo($"进程[Id:{Process.Id},Name:{Process.ProcessName}]已经退出，退出码：{Process.ExitCode}。");
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
            Process.Kill(true);
            base.Stop();
        }
    }
}
