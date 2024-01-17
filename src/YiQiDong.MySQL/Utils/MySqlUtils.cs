using Quick.Shell.Utils;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;
using YiQiDong.Agent;
using System.Runtime.InteropServices;

namespace YiQiDong.MySQL.Utils;

public class MySqlUtils
{
    public static void Init()
    {
        var imageFolder = AgentContext.Container.ImageFolder;
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

    private static bool IsRunAsRoot()
    {
        AgentContext.LogInfo("正在检查当前用户...");
        var tmpPsi = new ProcessStartInfo("whoami");
        tmpPsi.RedirectStandardOutput = true;
        var tmpProcess = Process.Start(tmpPsi);
        var account = tmpProcess.StandardOutput.ReadToEnd();
        return account.Trim() == "root";
    }

    private static void outputNotSupportOsAndArchitecture()
    {
        AgentContext.LogWarn($"不支持的操作系统[{RuntimeInformation.OSDescription}]+平台架构[{RuntimeInformation.OSArchitecture}]。");
    }


    public static ProcessStartInfo GetMySqldPsi(params string[] arguments)
    {
        var imageFolder = AgentContext.Container.ImageFolder;
        var dataFolder = Functions.Config.Instance.GetDataFolder();
        var process_filename = "";
        var process_arguments = new List<string>
            {
                $"--defaults-file={Path.Combine(dataFolder, "my.ini")}",
                $"--datadir={Path.Combine(dataFolder, "data")}",
                "--console"
            };
        if (arguments != null)
            process_arguments.AddRange(arguments);

        if (OperatingSystem.IsWindows())
        {
            process_filename = Path.Combine(imageFolder, "bin", "mysqld.exe");
        }
        else if (OperatingSystem.IsLinux())
        {
            process_filename = Path.Combine(imageFolder, "bin", "mysqld");
            //为进程添加可执行权限
            UnixUtils.AddExecutePermissionToFile(process_filename);
            if (IsRunAsRoot())
                process_arguments.Add("--user=root");
            process_arguments.Add($"--basedir={imageFolder}");
            process_arguments.Add($"--socket={Path.Combine(dataFolder, "mysqld.sock")}");
            process_arguments.Add("--secure-file-priv=");
        }
        else
        {
            outputNotSupportOsAndArchitecture();
        }
        var psi = ProcessUtils.CreateProcessStartInfo(process_filename, process_arguments.ToArray());
        ProcessUtils.ProcessProcessStartInfo(psi);
        psi.WorkingDirectory = dataFolder;
        return psi;
    }

    public static ProcessStartInfo GetMySqlAdminPsi(string user, string password, params string[] arguments)
    {
        var imageFolder = AgentContext.Container.ImageFolder;
        var dataFolder = Functions.Config.Instance.GetDataFolder();
        var process_filename = "";
        var process_arguments = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            process_filename = Path.Combine(imageFolder, "bin", "mysqladmin.exe");
            process_arguments.Add($"--host={Functions.Config.Instance.GetConnectHost()}");
            process_arguments.Add($"--port={Functions.Config.Instance.GetConnectPort()}");
        }
        else if (OperatingSystem.IsLinux())
        {
            process_filename = Path.Combine(imageFolder, "bin", "mysqladmin");
            process_arguments.Add($"--socket={dataFolder}/mysqld.sock");
            //为进程添加可执行权限
            UnixUtils.AddExecutePermissionToFile(process_filename);
        }
        else
        {
            throw new NotSupportedException();
        }
        if (!string.IsNullOrEmpty(user))
            process_arguments.Add($"--user={user}");
        if (!string.IsNullOrEmpty(password))
            process_arguments.Add($"--password={password}");
        if (arguments != null)
            process_arguments.AddRange(arguments);

        var psi = ProcessUtils.CreateProcessStartInfo(process_filename, process_arguments.ToArray());
        ProcessUtils.ProcessProcessStartInfo(psi);
        psi.WorkingDirectory = dataFolder;
        return psi;
    }

    public static void ModifyPassword(string user, string oldPassword, string newPassword)
    {

        var psi = GetMySqlAdminPsi(
            user,
            oldPassword,
            "password", newPassword);
        var ret = ProcessUtils.ExecuteProcessStartInfo(psi);
        if (ret.ExitCode == 0)
            return;
        throw new IOException($"修改密码时出错，原因：{ret.Output}{ret.Error}");
    }
}
