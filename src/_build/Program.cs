using Microsoft.Win32;
using Quick.Build;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

var appFolder = QbFolder.GetAppFolder();
if (appFolder == Environment.CurrentDirectory)
    Environment.CurrentDirectory = Path.GetFullPath("../../../../../");

string URL_TEMPLATE_WINDOWS = "{0}MySQL-{1}.{2}/mysql-{1}.{2}.{3}-{4}.zip";
string URL_TEMPLATE_LINUX_V5_7 = "{0}MySQL-{1}.{2}/mysql-{1}.{2}.{3}-{4}.tar.gz";
string URL_TEMPLATE_LINUX_V8 = "{0}MySQL-{1}.{2}/mysql-{1}.{2}.{3}-{4}.tar.xz";
string DATA_FILE_V5_7 = "src/YiQiDong.MySQL/Resource/data_v5.7.zip";
string DATA_FILE_V8 = "src/YiQiDong.MySQL/Resource/data_v8.zip";

Console.WriteLine("----------------------------------");
Console.WriteLine("  欢迎使用MySQL编译脚本");
Console.WriteLine("----------------------------------");
Version version;
HttpClient httpClient = new HttpClient();

Console.WriteLine("请选择要编译的MySQL版本：");
var mysqlVersion = QbSelect.ArrowSelect(new Dictionary<string, string>()
{
    ["5.7"] = "5.7",
    ["8.0"] = "8.0"
}.ToArray(), selectedForegroundColor: ConsoleColor.Green);

//Console.WriteLine($"获取MySQL {mysqlVersion}最新版本号中...");
//var mysqlVersionHtml = httpClient.GetStringAsync($"https://dev.mysql.com/downloads/mysql/{mysqlVersion}.html?tpl=version&os=3&osva=").Result;
//var mysqlVersionRegex = new Regex(@"MySQL Community Server (?<version>\d+\.\d+.\d+)");
//var mysqlVersionStr = mysqlVersionRegex.Match(mysqlVersionHtml).Groups["version"].Value;
//version= Version.Parse(mysqlVersionStr);
//Console.WriteLine($"MySQL {mysqlVersion}的最新版本号是: {mysqlVersionStr}");
switch(mysqlVersion)
{
    case "5.7":
        version = Version.Parse("5.7.38");
        break;
    case "8.0":
    default:
        version = Version.Parse("8.0.28");
        break;
}

Console.WriteLine("请选择镜像站：");
var mirrorUrl = QbSelect.ArrowSelect(new Dictionary<string, string>()
{
    ["https://dev.mysql.com/get/Downloads/"] = "MySQL官方网站",
    ["http://mirrors.163.com/mysql/Downloads/"] = "网易开源镜像站",
    ["https://mirrors.cloud.tencent.com/mysql/downloads/"] = "腾讯软件源"
}.ToArray(), selectedForegroundColor: ConsoleColor.Green);

Console.WriteLine("请选择运行平台：");
var platform = QbSelect.ArrowSelect(new Dictionary<string, string>()
{
    ["win_x64"] = "Windows(64位)",
    ["linux_x64"] = "Linux(64位)"
}.ToArray(), selectedForegroundColor: ConsoleColor.Green);

var folder = Path.Combine(Environment.CurrentDirectory, "bin", "MySQL");
if (Directory.Exists(folder))
{
    Console.WriteLine($"正在清理目录...");
    Directory.Delete(folder, true);
}
Directory.CreateDirectory(folder);

switch (platform)
{
    case "win_x64":
        {
            //开始下载win_x64版本                    
            var url = string.Format(URL_TEMPLATE_WINDOWS, mirrorUrl, version.Major, version.Minor, version.Build, "winx64");
            var file = Path.Combine(folder, Path.GetFileName(url));
            var winFolder = Path.Combine(folder, "mysql-win_x64");
            Console.WriteLine($"正在从[{url}]下载文件...");
            using (var fs = File.OpenWrite(file))
            using (var ns = httpClient.GetStreamAsync(url).Result)
                ns.CopyTo(fs);
            Console.WriteLine($"正在解压文件[{file}]...");
            ZipFile.ExtractToDirectory(file, folder);
            Thread.Sleep(1000);
            Directory.Move(Path.Combine(folder, Path.GetFileNameWithoutExtension(file)), winFolder);
            QbFolder.DeleteFolders(winFolder, "docs");
            QbFolder.DeleteFolders(winFolder, "include");
            File.Move(Path.Combine(winFolder, "bin", "mysqld.exe"), Path.Combine(winFolder, "mysqld.exe"));
            QbFile.DeleteFiles(Path.Combine(winFolder, "bin"), "*.exe");
            QbFile.DeleteFiles(Path.Combine(winFolder, "bin"), "*.lib");
            QbFile.DeleteFiles(Path.Combine(winFolder, "bin"), "*.pdb");
            QbFile.DeleteFiles(Path.Combine(winFolder, "bin"), "*debug.dll");
            QbFolder.Copy("src/YiQiDong.MySQL/Resource/mysql-win_x64/bin", Path.Combine(winFolder, "bin"));
            File.Move(Path.Combine(winFolder, "mysqld.exe"), Path.Combine(winFolder, "bin", "mysqld.exe"));
            QbFolder.DeleteFolders(Path.Combine(winFolder, "lib", "plugin"), "debug");
            QbFile.DeleteFiles(Path.Combine(winFolder, "lib", "plugin", "debug"), "*.pdb");
            QbFolder.DeleteFolders(Path.Combine(winFolder, "lib"), "mecab");
            QbFile.DeleteFiles(Path.Combine(winFolder, "lib"), "*.lib");
            QbFile.DeleteFiles(Path.Combine(winFolder, "lib"), "libmysql.dll");
            File.Delete(file);
            break;
        }
    case "linux_x64":
        {
            var linuxFolder = Path.Combine(folder, "mysql-linux_x64");
            var file = string.Empty;
            var url = string.Empty;

            //开始下载linux_x64版本
            //如果是8.0以上版本
            if (version >= new Version(8, 0))
            {
                url = string.Format(URL_TEMPLATE_LINUX_V8, mirrorUrl, version.Major, version.Minor, version.Build, "linux-glibc2.12-x86_64");
                file = Path.Combine(folder, Path.GetFileName(url));
                Console.WriteLine($"正在从[{url}]下载文件...");
                using (var fs = File.OpenWrite(file))
                using (var ns = httpClient.GetStreamAsync(url).Result)
                    ns.CopyTo(fs);
                Console.WriteLine($"正在解压文件[{file}]...");

                var tarFile = Path.Combine(folder, Path.GetFileNameWithoutExtension(file));
                using (var fileStream = File.Open(file, FileMode.Open))
                using (var xzStream = new SharpCompress.Compressors.Xz.XZStream(fileStream))
                using (var tarFileStream = File.OpenWrite(tarFile))
                    xzStream.CopyTo(tarFileStream);

                Console.WriteLine($"正在解压文件[{tarFile}]...");
                using (var tarArchive = SharpCompress.Archives.Tar.TarArchive.Open(tarFile))
                {
                    foreach (var tarEntry in tarArchive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        tarEntry.WriteToDirectory(folder, new ExtractionOptions()
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
                Thread.Sleep(1000);
                Directory.Move(Path.Combine(folder, Path.GetFileNameWithoutExtension(tarFile)), linuxFolder);
                File.Delete(tarFile);
            }
            //如果是5.7以上版本
            else if (version >= new Version(5, 7))
            {
                url = string.Format(URL_TEMPLATE_LINUX_V5_7, mirrorUrl, version.Major, version.Minor, version.Build, "linux-glibc2.12-x86_64");
                file = Path.Combine(folder, Path.GetFileName(url));
                Console.WriteLine($"正在从[{url}]下载文件...");
                using (var fs = File.OpenWrite(file))
                using (var ns = httpClient.GetStreamAsync(url).Result)
                    ns.CopyTo(fs);

                Console.WriteLine($"正在解压文件[{file}]...");
                var tarFile = Path.Combine(folder, Path.GetFileNameWithoutExtension(file));

                using (var fileStream = File.Open(file, FileMode.Open))
                using (var gzStream = new GZipStream(fileStream, CompressionMode.Decompress))
                using (var tarFileStream = File.OpenWrite(tarFile))
                    gzStream.CopyTo(tarFileStream);

                Console.WriteLine($"正在解压文件[{tarFile}]...");
                using (var tarArchive = SharpCompress.Archives.Tar.TarArchive.Open(tarFile))
                {
                    foreach (var tarEntry in tarArchive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        tarEntry.WriteToDirectory(folder, new ExtractionOptions()
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
                Thread.Sleep(1000);
                Directory.Move(Path.Combine(folder, Path.GetFileNameWithoutExtension(tarFile)), linuxFolder);
                File.Delete(tarFile);
            }
            QbFolder.DeleteFolders(linuxFolder, "docs");
            QbFolder.DeleteFolders(linuxFolder, "include");
            QbFolder.DeleteFolders(linuxFolder, "man");
            QbFolder.DeleteFolders(linuxFolder, "support-files");
            File.Move(Path.Combine(linuxFolder, "bin", "mysqld"), Path.Combine(linuxFolder, "mysqld"));
            QbFolder.DeleteFolders(linuxFolder, "bin");
            Directory.CreateDirectory(Path.Combine(linuxFolder, "bin"));
            File.Move(Path.Combine(linuxFolder, "mysqld"), Path.Combine(linuxFolder, "bin", "mysqld"));
            QbFolder.DeleteFolders(Path.Combine(linuxFolder, "lib", "plugin"), "debug");
            QbFile.DeleteFiles(Path.Combine(linuxFolder, "lib"), "*.a");
            QbFile.DeleteFiles(Path.Combine(linuxFolder, "lib"), "libmysqlclient.so.*");
            QbFolder.DeleteFolders(Path.Combine(linuxFolder, "lib"), "mecab");
            QbFolder.DeleteFolders(Path.Combine(linuxFolder, "lib"), "pkgconfig");
            QbFolder.Copy("src/YiQiDong.MySQL/Resource/mysql-linux_x64/lib", Path.Combine(linuxFolder, "lib"));
            File.Delete(file);
            break;
        }
}

var dataFile = string.Empty;
//如果是8.0以上版本
if (version >= new Version(8, 0))
    dataFile = DATA_FILE_V8;
else
    dataFile = DATA_FILE_V5_7;
Console.WriteLine($"正在解压文件[{dataFile}]...");
ZipFile.ExtractToDirectory(dataFile, folder);

Console.WriteLine("正在发布YiQiDong.MySQL项目...");
QbCommand.Run("dotnet", $"publish -c Release -o {folder} src/YiQiDong.MySQL");
var versionString = $"{version}_{DateTime.Now.ToString("yyyyMMdd")}";
QbJson.WriteString(Path.Combine(folder, "YiQiDong.Image.json"), "Version", versionString);
QbJson.Write(Path.Combine(folder, "YiQiDong.Image.json"), "Platform", new[] { platform });

var outFile = $"bin/MySQL_{versionString}_{platform}.ymg";
Console.WriteLine("正在制作弈启动镜像...");
using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
{
    archive.AddAllFromDirectory(folder);
    archive.SaveTo(outFile, CompressionType.LZMA);
}
QbFile.ChangeHeader(outFile, "yz");

Console.WriteLine("完成");
//如果是在Windows平台，则打开窗口
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    try { QbCommand.Run("Explorer", @"bin"); }
    catch { }
}