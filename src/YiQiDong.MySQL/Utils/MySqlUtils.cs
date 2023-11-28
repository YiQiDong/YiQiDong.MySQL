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
            process_arguments.Add($" --basedir={imageFolder}");
            process_arguments.Add($" --socket={Path.Combine(dataFolder, "mysqld.sock")}");
            process_arguments.Add(" --secure-file-priv=");
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

    public static ProcessStartInfo GetMySqlAdminPsi(string host, int port, string user, string password,
        params string[] arguments)
    {
        var imageFolder = AgentContext.Container.ImageFolder;
        var dataFolder = Functions.Config.Instance.GetDataFolder();
        var process_filename = "";
        var process_arguments = new List<string>()
        {
            $"--host={host}",
            $"--port={port}",
            $"--user={user}",
            $"--password={password}"
        };
        if (arguments != null)
            process_arguments.AddRange(arguments);
        if (OperatingSystem.IsWindows())
        {
            process_filename = Path.Combine(imageFolder, "bin", "mysqladmin.exe");
        }
        else if (OperatingSystem.IsLinux())
        {
            process_filename = Path.Combine(imageFolder, "bin", "mysqladmin");
            //为进程添加可执行权限
            UnixUtils.AddExecutePermissionToFile(process_filename);
        }
        else
        {
            throw new NotSupportedException();
        }
        var psi = ProcessUtils.CreateProcessStartInfo(process_filename, process_arguments.ToArray());
        ProcessUtils.ProcessProcessStartInfo(psi);
        psi.WorkingDirectory = dataFolder;
        return psi;
    }
}
