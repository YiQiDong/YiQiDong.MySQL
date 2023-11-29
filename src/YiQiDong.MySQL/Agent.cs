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
using System.Threading;

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
                AddFunction(Config.Instance);
                AddFunction(new PasswordManager(), true);
                AddFunction(new SqlQuery());
                MySqlUtils.Init();
            }
        }

        public override void Start()
        {
            Task.Run(() =>
            {
                try
                {
                    innnerStart();
                    base.Start();
                }
                catch (Exception ex)
                {
                    AgentContext.LogError($"启动容器时失败，原因：{ex}");
                }
            });
        }

        private void innnerStart()
        {
            if (!AgentContext.Container.AutoStart)
                return;

            var imageFolder = AgentContext.Container.ImageFolder;
            var dataFolder = Config.Instance.GetDataFolder();
            //检查复制my.ini文件
            FilsSystemUtils.CopyFile(Path.Combine(imageFolder, "my.ini"), dataFolder);
            //是否已初始化
            var initialized = true;
            //检查数据库是否初始化，如果不存在，则初始化
            if (!Directory.Exists(Path.Combine(dataFolder, "data")))
            {
                initialized = false;
                AgentContext.LogInfo("正在初始化数据库...");
                var ret = ProcessUtils.ExecuteProcessStartInfo(MySqlUtils.GetMySqldPsi("--initialize"));
                if (ret.ExitCode == 0)
                {
                    AgentContext.LogInfo("初始化数据库时成功。");
                    var output = $"{ret.Output}{ret.Error}";
                    AgentContext.LogInfo(output);
                    //[Note] [MY-010454] [Server] A temporary password is generated for root@localhost: PhhZ4mdQ6t-X
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!line.Contains("temporary password"))
                            continue;
                        var tmpPassword = line.Split(": ", StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                        AgentContext.LogInfo("临时密码：" + tmpPassword);
                        Config.Instance.UpdatePassword(tmpPassword);
                        break;
                    }
                }
                else
                {
                    AgentContext.LogError("初始化数据库时出错，原因：" + ret.Error);
                }
            }

            Process = Process.Start(MySqlUtils.GetMySqldPsi());
            Process.EnableRaisingEvents = true;
            Process.OutputDataReceived += Process_OutputDataReceived;
            Process.ErrorDataReceived += Process_ErrorDataReceived;
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
            AgentContext.LogInfo("MySQL监听端口：" + Config.Instance.GetConnectPort());
            AgentContext.LogInfo($"MySQL服务进程[Id:{Process.Id},Name:{Process.ProcessName}]已经启动。");
            Process.Exited += Process_Exited;

            //等待连接可用
            while (!Process.HasExited)
            {
                Thread.Sleep(1000);
                var ret = ProcessUtils.ExecuteProcessStartInfo(MySqlUtils.GetMySqlAdminPsi(
                    Config.Instance.GetConnectHost(),
                    Config.Instance.GetConnectPort(),
                    "root",
                    Config.Instance.GetPassword(),
                    "ping"));
                if (ret.ExitCode == 0)
                    break;
            }
            if (Process.HasExited)
                return;
            //如果是第一次初始化，则修改密码，并允许root远程连接
            if (!initialized)
            {
                AgentContext.LogInfo("正在修改自动生成的临时密码...");
                var newPassword = Guid.NewGuid().ToString("N");
                MySqlUtils.ModifyPassword(
                    Config.Instance.GetConnectHost(),
                    Config.Instance.GetConnectPort(),
                    "root",
                    Config.Instance.GetPassword(),
                    newPassword);
                Config.Instance.UpdatePassword(newPassword);
                var connectionStringBuilder = new MySqlConnectionStringBuilder()
                {
                    Server = Config.Instance.GetConnectHost(),
                    Port = Convert.ToUInt32(Config.Instance.GetConnectPort()),
                    Database = "mysql",
                    UserID = "root",
                    Password = newPassword,
                    SslMode = MySqlSslMode.None
                };
                AgentContext.LogInfo("正在允许root用户远程登录...");
                var connectionString = connectionStringBuilder.ConnectionString;

                var sql = "update user set host = '%' where user = 'root';flush privileges;";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand(sql, connection))
                        cmd.ExecuteNonQuery();
                }
            }
            AgentContext.LogInfo("[MySQL服务启动完成]");
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
