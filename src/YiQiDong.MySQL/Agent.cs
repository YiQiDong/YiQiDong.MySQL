using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using YiQiDong.Core;
using YiQiDong.Core.Utils;
using YiQiDong.Agent;
using MySqlConnector;
using Quick.Shell.Utils;
using System.Linq;
using YiQiDong.MySQL.Functions;
using YiQiDong.MySQL.Utils;

namespace YiQiDong.MySQL
{
    public class Agent : AbstractAgent
    {
        private string imageFolder;
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
                imageFolder = AgentContext.Container.ImageFolder;
                var containerFolder = AgentContext.Container.ContainerFolder;

                AddFunction(new Functions.Config("配置修改", imageFolder, containerFolder), false);
                AddFunction(new Functions.Config("配置查看", imageFolder, containerFolder), true);

                AddFunction(new Functions.PasswordManager(), true);
                AddFunction(new Functions.SqlQuery());

                //Linux系统上添加LD_LIBRARY_PATH环境变量
                if (OperatingSystem.IsLinux())
                {
                    var mysqlLibDir = Path.Combine(imageFolder, "lib");
                    //添加PATH环境变量
                    var path = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
                    if (string.IsNullOrEmpty(path))
                        path = mysqlLibDir;
                    else
                        path = $"{path}:{mysqlLibDir}";
                    Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", path);
                }
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

        private void innnerStart()
        {
            if (Process != null)
                return;
            if (!AgentContext.Container.AutoStart)
                return;

            var imageFolder = AgentContext.Container.ImageFolder;
            var dataFolder = Functions.Config.Instance.GetDataFolder();
            //检查数据库是否初始化，如果不存在，则初始化
            if (!Directory.Exists(Path.Combine(dataFolder, "data")))
            {
                AgentContext.LogInfo("正在初始化数据库...");
                var ret = ProcessUtils.ExecuteProcessStartInfo(MySqlUtils.GetMySqldPsi("--initialize"));
                if (ret.ExitCode == 0)
                {
                    AgentContext.LogInfo("初始化数据库时成功，修改密码后才能正常登录使用。");
                    var output = $"{ret.Output}{ret.Error}";
                    var tmpPassword = output.Split(" ", StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    Config.Instance.UpdatePassword(tmpPassword);
                }
                else
                {
                    AgentContext.LogError("初始化数据库时出错，原因：" + ret.Error);
                }
            }
            //检查复制my.ini文件
            FilsSystemUtils.CopyFile(Path.Combine(imageFolder, "my.ini"), dataFolder);

            Process = Process.Start(MySqlUtils.GetMySqldPsi());
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

            host = Config.Instance.GetConnectHost();
            port = Config.Instance.GetConnectPort();
            user = "root";
            password = Config.Instance.GetPassword();
            try
            {
                var psi = MySqlUtils.GetMySqlAdminPsi(host, port, user, password, "shutdown");
                var ret = ProcessUtils.ExecuteProcessStartInfo(psi);
                if (ret.ExitCode != 0)
                    throw new IOException($"停止MySQL时出错，原因：{ret.Output}{ret.Error}");
                //等待30秒
                Process.WaitForExit(30 * 1000);
            }
            catch (Exception ex)
            {
                AgentContext.LogError(ExceptionUtils.GetExceptionMessage(ex));
            }
            if (Process == null
                || Process.HasExited)
                return;
            Process.Kill(true);
            base.Stop();
        }
    }
}
