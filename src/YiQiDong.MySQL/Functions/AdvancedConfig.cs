using Quick.Fields;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YiQiDong.Core;
using YiQiDong.Protocol.V1.Model;

namespace YiQiDong.MySQL.Functions
{
    class AdvancedConfig : AbstractFunction
    {
        public override string Name => "高级配置";
        
        private const string CONTENT_KEY = "Content";
        private string containerConfigFile;
        public AdvancedConfig(string containerFolder)
        {
            containerConfigFile = Path.Combine(containerFolder, Config.CONFIG_FILE);
        }

        private List<FieldForGet> innerGet(FunctionRequest request, bool isReadOnly = false)
        {
            List<FieldForGet> list = new List<FieldForGet>();
            if (!File.Exists(containerConfigFile))
            {
                list.Add(new FieldForGet() { Name = "失败", Description = $"配置文件[{Config.CONFIG_FILE}]不存在！", Input_ReadOnly = true, Type = FieldType.Alert });
                return list;
            }
            string tmpKey;
            tmpKey = CONTENT_KEY;
            list.Add(new FieldForGet()
            {
                Id = tmpKey,
                Name = "内容",
                Type = FieldType.InputTextArea,
                Input_ReadOnly = isReadOnly,
                Value = request == null ? File.ReadAllText(containerConfigFile) : request.GetFieldValue(tmpKey),
                Input_AllowBlank = false,
                Description = "my.ini配置文件的内容"
            });
            return list;
        }

        public override FieldForGet[] Get()
        {
            var isReadOnly = Agent.Instance.ContainerInfo.AutoStart;
            var list = innerGet(null, isReadOnly);
            if (!isReadOnly)
                addSaveButton(list);
            return list.ToArray();
        }

        public override FieldForGet[] Post(FunctionRequest request)
        {
            var list = innerGet(request);
            if (request.IsFieldIdsMatch("Save"))
            {
                
                if (File.Exists(containerConfigFile))
                {
                    File.WriteAllText(containerConfigFile, request.GetFieldValue(CONTENT_KEY));
                    //保存成功后重新加载配置文件
                    Config.Instance.RefreshProperties();
                    list.Add(new FieldForGet()
                    {
                        Name = "保存成功",
                        Description = $"配置文件[{Config.CONFIG_FILE}]保存成功！",
                        Type = FieldType.MessageBox
                    });
                }
                else
                {
                    list.Add(new FieldForGet()
                    {
                        Name = "错误",
                        Description = $"配置文件[{Config.CONFIG_FILE}]不存在！",
                        Type = FieldType.Alert,
                        Input_ReadOnly = true
                    });
                }
                addSaveButton(list);
            }
            return list.ToArray();
        }

        private void addSaveButton(List<FieldForGet> list)
        {
            list.Add(new FieldForGet() { Id = "Save", Name = "保存", Type = FieldType.Button });
        }
    }
}
