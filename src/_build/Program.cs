using Quick.Build;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

var productDir = "YiQiDong.MySQL";
var appFolder = QbFolder.GetAppFolder();
if (appFolder == Environment.CurrentDirectory)
    Environment.CurrentDirectory = Path.GetFullPath("../../../../../");
//https://dev.mysql.com/get/Downloads/MySQL-5.7/mysql-5.7.42-winx64.zip
string URL_TEMPLATE_WINDOWS = "{0}MySQL-{1}.{2}/mysql-{1}.{2}.{3}-{4}.zip";
string URL_TEMPLATE_LINUX_V5_7 = "{0}MySQL-{1}.{2}/mysql-{1}.{2}.{3}-{4}.tar.gz";
string URL_TEMPLATE_LINUX_V8 = "{0}MySQL-{1}.{2}/mysql-{1}.{2}.{3}-{4}.tar.xz";
string DATA_FILE_V5_7 = $"src/{productDir}/Resource/data_v5.7.zip";
string DATA_FILE_V8 = $"src/{productDir}/Resource/data_v8.zip";

Console.WriteLine("----------------------------------");
Console.WriteLine("  欢迎使用MySQL编译脚本");
Console.WriteLine("----------------------------------");
Version version;
HttpClient httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("text/html"));
httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/xhtml+xml"));
httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/xml;q=0.9"));
httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("image/avif"));
httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("image/webp"));
httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("image/apng"));
httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("*/*;q=0.8"));
httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/signed-exchange;v=b3;q=0.7"));
httpClient.DefaultRequestHeaders.AcceptEncoding.Add(StringWithQualityHeaderValue.Parse("*"));
httpClient.DefaultRequestHeaders.AcceptLanguage.Add(StringWithQualityHeaderValue.Parse("en-US"));
httpClient.DefaultRequestHeaders.AcceptLanguage.Add(StringWithQualityHeaderValue.Parse("en;q=0.9"));
httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
httpClient.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse("Mozilla/5.0"));
httpClient.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse("(Windows NT 10.0; Win64; x64)"));
httpClient.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse("AppleWebKit/537.36"));
httpClient.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse("(KHTML, like Gecko)"));
httpClient.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse("Chrome/113.0.0.0"));
httpClient.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse("Safari/537.36"));

Console.WriteLine("请选择要编译的MySQL版本：");
var mysqlVersion = QbSelect.ArrowSelect(new Dictionary<string, string>()
{
    ["5.7"] = "5.7",
    ["8.0"] = "8.0"
}.ToArray(), selectedForegroundColor: ConsoleColor.Green);

Console.WriteLine($"获取MySQL {mysqlVersion}最新版本号中...");
var mysqlVersionHtml = httpClient.GetStringAsync($"https://dev.mysql.com/downloads/mysql/{mysqlVersion}.html?tpl=version&os=3&osva=").Result;
var mysqlVersionRegex = new Regex(@"MySQL Community Server (?<version>\d+\.\d+.\d+)");
var mysqlVersionStr = mysqlVersionRegex.Match(mysqlVersionHtml).Groups["version"].Value;
version= Version.Parse(mysqlVersionStr);
Console.WriteLine($"MySQL {mysqlVersion}的最新版本号是: {mysqlVersionStr}");

Console.WriteLine("请选择镜像站：");
var mirrorUrl = QbSelect.ArrowSelect(new Dictionary<string, string>()
{
    ["https://dev.mysql.com/get/Downloads/"] = "MySQL官方网站"
}.ToArray(), selectedForegroundColor: ConsoleColor.Green);

Console.WriteLine("请选择运行平台(一个都不选代表全选)：");
var ridDict = new Dictionary<string, string>()
{
    ["win-x64"] = "win-x64",
    ["linux-x64"] = "linux-x64"
};
//如果是8.0以上版本，才有arm64架构的二进制文件;
if (version >= Version.Parse("8.0"))
    ridDict["linux-arm64"] = "linux-arm64";
var rids = QbSelect.MultiSelect(ridDict.ToArray(), selectedForegroundColor: ConsoleColor.Green);
if (rids == null || rids.Length == 0)
    rids = ridDict.Keys.ToArray();

var binFolder = Path.Combine(Environment.CurrentDirectory, "bin");
if (!Directory.Exists(binFolder))
    Directory.CreateDirectory(binFolder);

foreach (var rid in rids)
{
    Console.WriteLine($"开始打包[{rid}]...");
    
    var folder = Path.Combine(binFolder, "MySQL");
    if (Directory.Exists(folder))
    {
        Console.WriteLine($"正在清理目录...");
        Directory.Delete(folder, true);
    }
    switch (rid)
    {
        case "win-x64":
            {
                //开始下载win-x64版本                    
                var url = string.Format(URL_TEMPLATE_WINDOWS, mirrorUrl, version.Major, version.Minor, version.Build, "winx64");
                var file = Path.Combine(binFolder, Path.GetFileName(url));
                Console.WriteLine($"正在从[{url}]下载文件...");
                using (var fs = File.OpenWrite(file))
                using (var ns = httpClient.GetStreamAsync(url).Result)
                    ns.CopyTo(fs);
                Console.WriteLine($"正在解压文件[{file}]...");
                ZipFile.ExtractToDirectory(file, binFolder);
                Directory.Move(Path.Combine(binFolder, Path.GetFileNameWithoutExtension(file)), folder);
                Thread.Sleep(1000);
                QbFolder.DeleteFolders(folder, "docs");
                QbFolder.DeleteFolders(folder, "include");
                File.Move(Path.Combine(folder, "bin", "mysqld.exe"), Path.Combine(folder, "mysqld.exe"));
                File.Move(Path.Combine(folder, "bin", "mysqladmin.exe"), Path.Combine(folder, "mysqladmin.exe"));
                QbFile.DeleteFiles(Path.Combine(folder, "bin"), "*.exe");
                QbFile.DeleteFiles(Path.Combine(folder, "bin"), "*.lib");
                QbFile.DeleteFiles(Path.Combine(folder, "bin"), "*.pdb");
                QbFile.DeleteFiles(Path.Combine(folder, "bin"), "*debug.dll");
                File.Move(Path.Combine(folder, "mysqld.exe"), Path.Combine(folder, "bin", "mysqld.exe"));
                File.Move(Path.Combine(folder, "mysqladmin.exe"), Path.Combine(folder, "bin", "mysqladmin.exe"));
                QbFolder.DeleteFolders(Path.Combine(folder, "lib", "plugin"), "debug");
                QbFile.DeleteFiles(Path.Combine(folder, "lib", "plugin", "debug"), "*.pdb");
                QbFolder.DeleteFolders(Path.Combine(folder, "lib"), "mecab");
                QbFile.DeleteFiles(Path.Combine(folder, "lib"), "*.lib");
                QbFile.DeleteFiles(Path.Combine(folder, "lib"), "libmysql.dll");
                File.Delete(file);
                break;
            }
        default:
            {
                var file = string.Empty;
                var url = string.Empty;

                //开始下载linux_x64版本
                //如果是8.0以上版本
                if (version >= new Version(8, 0))
                {
                    url = string.Format(URL_TEMPLATE_LINUX_V8, mirrorUrl,
                        version.Major, version.Minor, version.Build,
                        rid == "linux-x64" ? "linux-glibc2.12-x86_64" : "linux-glibc2.17-aarch64");
                    file = Path.Combine(binFolder, Path.GetFileName(url));
                    Console.WriteLine($"正在从[{url}]下载文件...");
                    using (var fs = File.OpenWrite(file))
                    using (var ns = httpClient.GetStreamAsync(url).Result)
                        ns.CopyTo(fs);
                    Console.WriteLine($"正在解压文件[{file}]...");

                    var tarFile = Path.Combine(binFolder, Path.GetFileNameWithoutExtension(file));
                    using (var fileStream = File.Open(file, FileMode.Open))
                    using (var xzStream = new SharpCompress.Compressors.Xz.XZStream(fileStream))
                    using (var tarFileStream = File.OpenWrite(tarFile))
                        xzStream.CopyTo(tarFileStream);

                    Console.WriteLine($"正在解压文件[{tarFile}]...");
                    using (var tarArchive = SharpCompress.Archives.Tar.TarArchive.Open(tarFile))
                    {
                        foreach (var tarEntry in tarArchive.Entries.Where(entry => !entry.IsDirectory))
                        {
                            tarEntry.WriteToDirectory(binFolder, new ExtractionOptions()
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                    Thread.Sleep(1000);
                    Directory.Move(Path.Combine(binFolder, Path.GetFileNameWithoutExtension(tarFile)), folder);
                    File.Delete(tarFile);
                }
                //如果是5.7以上版本
                else if (version >= new Version(5, 7))
                {
                    url = string.Format(URL_TEMPLATE_LINUX_V5_7, mirrorUrl,
                        version.Major, version.Minor, version.Build,
                        rid == "linux-x64" ? "linux-glibc2.12-x86_64" : "linux-glibc2.12-aarch64");
                    file = Path.Combine(binFolder, Path.GetFileName(url));
                    Console.WriteLine($"正在从[{url}]下载文件...");
                    using (var fs = File.OpenWrite(file))
                    using (var ns = httpClient.GetStreamAsync(url).Result)
                        ns.CopyTo(fs);

                    Console.WriteLine($"正在解压文件[{file}]...");
                    var tarFile = Path.Combine(binFolder, Path.GetFileNameWithoutExtension(file));

                    using (var fileStream = File.Open(file, FileMode.Open))
                    using (var gzStream = new GZipStream(fileStream, CompressionMode.Decompress))
                    using (var tarFileStream = File.OpenWrite(tarFile))
                        gzStream.CopyTo(tarFileStream);

                    Console.WriteLine($"正在解压文件[{tarFile}]...");
                    using (var tarArchive = SharpCompress.Archives.Tar.TarArchive.Open(tarFile))
                    {
                        foreach (var tarEntry in tarArchive.Entries.Where(entry => !entry.IsDirectory))
                        {
                            tarEntry.WriteToDirectory(binFolder, new ExtractionOptions()
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                    Thread.Sleep(1000);
                    Directory.Move(Path.Combine(binFolder, Path.GetFileNameWithoutExtension(tarFile)), folder);
                    File.Delete(tarFile);
                }
                QbFolder.DeleteFolders(folder, "docs");
                QbFolder.DeleteFolders(folder, "include");
                QbFolder.DeleteFolders(folder, "man");
                QbFolder.DeleteFolders(folder, "support-files");
                File.Move(Path.Combine(folder, "bin", "mysqld"), Path.Combine(folder, "mysqld"));
                File.Move(Path.Combine(folder, "bin", "mysqladmin"), Path.Combine(folder, "mysqladmin"));
                QbFolder.DeleteFolders(folder, "bin");
                Directory.CreateDirectory(Path.Combine(folder, "bin"));
                File.Move(Path.Combine(folder, "mysqld"), Path.Combine(folder, "bin", "mysqld"));
                File.Move(Path.Combine(folder, "mysqladmin"), Path.Combine(folder, "bin", "mysqladmin"));
                QbFolder.DeleteFolders(Path.Combine(folder, "lib", "plugin"), "debug");
                QbFile.DeleteFiles(Path.Combine(folder, "lib"), "*.a");
                QbFile.DeleteFiles(Path.Combine(folder, "lib"), "libmysqlclient.so.*");
                QbFolder.DeleteFolders(Path.Combine(folder, "lib"), "mecab");
                QbFolder.DeleteFolders(Path.Combine(folder, "lib"), "pkgconfig");
                QbFolder.Copy($"src/{productDir}/Resource/mysql-linux_x64/lib", Path.Combine(folder, "lib"));
                File.Delete(file);
                break;
            }
    }

    Console.WriteLine("正在发布YiQiDong.MySQL项目...");
    QbCommand.Run("dotnet", $"publish src/{productDir} -c Release -o {folder} -r {rid} --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true");
    var versionString = version.ToString();
    var imageMetaFile = Path.Combine(folder, "YiQiDong.Image.json");
    QbJson.WriteString(imageMetaFile, "Version", versionString);
    QbJson.Write(imageMetaFile, "Platform", new[] { rid });
    //修改Agent的值
    if (rid.StartsWith("win-"))
        QbJson.WriteString(imageMetaFile, "AgentExecute", $"{productDir}.exe");
    else
        QbJson.WriteString(imageMetaFile, "AgentExecute", productDir);

    var outFile = $"bin/MySQL-{versionString}-{rid}.ymg";
    Console.WriteLine($"正在制作弈启动镜像[{rid}]...");
    using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
    {
        archive.AddAllFromDirectory(folder);
        archive.SaveTo(outFile, CompressionType.LZMA);
    }
}
Console.WriteLine("完成");
//如果是在Windows平台，则打开窗口
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    try { QbCommand.Run("Explorer", @"bin"); }
    catch { }
}