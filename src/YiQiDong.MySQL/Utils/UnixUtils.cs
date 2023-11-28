using Quick.Shell.Utils;
using System.Collections.Generic;

namespace YiQiDong.MySQL.Utils
{
    public class UnixUtils
    {
        private static Dictionary<string, string> fileNameReplaceDict = new Dictionary<string, string>()
        {
            [" "] = "\\ ",
            ["\""] = "\\\"",
            ["'"] = "\\'",
            ["`"] = "\\`"
        };

        /// <summary>
        /// 为文件添加可执行权限
        /// </summary>
        /// <param name="fileName"></param>
        public static void AddExecutePermissionToFile(string fileName)
        {
            foreach (var key in fileNameReplaceDict.Keys)
            {
                if (fileName.Contains(key))
                    fileName = fileName.Replace(key, fileNameReplaceDict[key]);
            }
            ProcessUtils.ExecuteShell($"chmod +x {fileName}");
        }
    }
}
