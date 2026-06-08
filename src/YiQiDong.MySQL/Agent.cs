using System.Diagnostics;
using YiQiDong.Core;
using YiQiDong.Core.Utils;
using YiQiDong.Agent;
using MySqlConnector;
using Quick.Shell.Utils;
using YiQiDong.MySQL.Functions;
using YiQiDong.MySQL.Utils;

namespace YiQiDong.MySQL
{
    public class Agent : AbstractAgent
    {
        private string[][] serveiceStartLogKeys =
        [
            [
                "Version:",
                "port:",
                "Source distribution"
            ],
            [
                "Version:",
                "port:",
                "MySQL Community Server"
            ]
        ];

        public static Agent Instance { get; private set; }
        /// <summary>
        /// MySQL服务是否已启动
        /// </summary>
        public bool MySqlServiceStarted { get; private set; } = false;
        private string imageFolder;

        public Process Process { get; set; }

        public Agent()
        {
            Instance = this;
        }

        public override void Init()
        {
            //注册编码提供程序(支持GB2312等编码)
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            base.Init();

            if(AgentContext.IsContainerRuning)
            {
                imageFolder = AgentContext.Container.ImageFolder;
                AddFunction(new Config());
                AddFunction(new PasswordManager(), true);
                AddFunction(new SqlQuery());
            }
            else
            {
                imageFolder = AppContext.BaseDirectory;
                var dataFolder = imageFolder;
                new Config(imageFolder, dataFolder);
            }
            MySqlUtils.Init(imageFolder);
        }

        public override void Start()
        {
            var dataFolder = Config.Instance.GetDataFolder();
            Config.Instance.RefreshProperties(dataFolder);
            Task.Run(() =>
            {
                try
                {
                    innnerStart(dataFolder);
                    base.Start();
                }
                catch (Exception ex)
                {
                    AgentContext.LogError($"启动容器时失败，原因：{ex}");
                }
            });
        }

        internal void innnerStart(string dataFolder)
        {
            if (AgentContext.IsContainerRuning && !AgentContext.Container.AutoStart)
                return;
                
            MySqlServiceStarted = false;
            //检查复制my.ini文件
            FileSystemUtils.CopyFile(Path.Combine(imageFolder, "my.ini"), dataFolder);
            //是否已初始化
            var initialized = true;
            //检查数据库是否初始化，如果不存在，则初始化
            if (!Directory.Exists(Path.Combine(dataFolder, "data")))
            {
                initialized = false;
                AgentContext.LogInfo("正在初始化数据库...");
                var ret = ProcessUtils.ExecuteProcessStartInfo(MySqlUtils.GetMySqldPsi(imageFolder, dataFolder, "--initialize"));
                if (ret.ExitCode == 0)
                {
                    AgentContext.LogInfo("初始化数据库时成功。");
                    var output = $"{ret.Output}{ret.Error}";
                    AgentContext.LogInfo(output);
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!line.Contains("temporary password"))
                            continue;
                        var tmpPassword = line.Split(": ", StringSplitOptions.RemoveEmptyEntries)
                            .LastOrDefault()
                            .Trim();
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
            Process = Process.Start(MySqlUtils.GetMySqldPsi(imageFolder, dataFolder));
            Process.EnableRaisingEvents = true;
            Process.OutputDataReceived += Process_OutputDataReceived;
            Process.ErrorDataReceived += Process_ErrorDataReceived;
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
            AgentContext.LogInfo("MySQL监听端口：" + Config.Instance.GetConnectPort());
            AgentContext.LogInfo($"MySQL服务进程[Id:{Process.Id},Name:{Process.ProcessName}]已经启动。");
            Process.Exited += Process_Exited;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            //等待服务启动完成
            while (!Process.HasExited && !MySqlServiceStarted)
            {
                Thread.Sleep(1000);
                var totalMinutes = stopwatch.Elapsed.TotalMinutes;
                if (totalMinutes > 1)
                {
                    AgentContext.LogWarn("MySQL服务经过了1分钟仍未启动完成...");
                    stopwatch.Restart();
                }
            }
            stopwatch.Stop();
            if (Process.HasExited)
                return;
            //如果是第一次初始化，则修改密码，并允许root远程连接
            if (!initialized)
            {
                AgentContext.LogInfo("正在修改自动生成的临时密码...");
                var newPassword = Guid.NewGuid().ToString("N");
                MySqlUtils.ModifyPassword(
                    imageFolder,
                    dataFolder,
                    "root",
                    Config.Instance.GetPassword(),
                    newPassword);
                Config.Instance.UpdatePassword(newPassword);
                AgentContext.LogInfo("当前密码：" + newPassword);
                var connectionStringBuilder = new MySqlConnectionStringBuilder()
                {
                    Server = Config.Instance.GetConnectHost(),
                    Port = Convert.ToUInt32(Config.Instance.GetConnectPort()),
                    Database = "mysql",
                    UserID = "root",
                    Password = newPassword,
                    SslMode = MySqlSslMode.None,
                    AllowPublicKeyRetrieval = true
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
            var line = e.Data;
            AgentContext.LogInfo(line);
            //5.7(源码构建):
            // [Info] Version: '5.7.44'  socket: '/var/lib/YiQiDong/Containers/MySQL-1/mysqld.sock'  port: 3306  Source distribution
            //5.7:
            // [Info] Version: '5.7.44'  socket: '/data/YiQiDong/Data/Containers/MySQL-1/mysqld.sock'  port: 3311  MySQL Community Server (GPL)
            //8.0:
            // [Info] 2024-01-17T09:05:29.462653Z 0 [System] [MY-010931] [Server] /newdisk/YiQiDong/Data/Images/MySQL/bin/mysqld: ready for connections. Version: '8.0.36'  socket: '/newdisk/YiQiDong/Data/Containers/MySQL-1/mysqld.sock'  port: 3311  MySQL Community Server - GPL.
            //判断MySQL服务启动完成
            if (!MySqlServiceStarted)
                MySqlServiceStarted = serveiceStartLogKeys.Any(t => t.All(u => line.Contains(u)));
        }

        private void delayStart()
        {
            Task.Delay(5000).ContinueWith(t =>
            {
                innnerStart(Config.Instance.GetDataFolder());
            });
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            AgentContext.LogInfo($"进程[Id:{Process.Id},Name:{Process.ProcessName}]已经退出，退出码：{Process.ExitCode}。");
            if (OperatingSystem.IsWindows())
            {
                switch (Convert.ToUInt32(Process.ExitCode))
                {
                    case 0xC0000135:
                        AgentContext.LogInfo($"DLL文件加载失败，请确保已安装正确的Visual C++运行库。下载地址：https://learn.microsoft.com/zh-cn/cpp/windows/latest-supported-vc-redist");
                        break;
                }
            }
            if (AgentContext.IsContainerRuning)
            {
                if (!AgentContext.Container.AutoStart)
                    return;
                delayStart();
            }
        }

        public override void Stop()
        {
            //发送shutdown
            string user;
            string password;
            var dataFolder = Config.Instance.GetDataFolder();
            user = "root";
            password = Config.Instance.GetPassword();
            try
            {
                var psi = MySqlUtils.GetMySqlAdminPsi(imageFolder, dataFolder, user, password, "shutdown");
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
