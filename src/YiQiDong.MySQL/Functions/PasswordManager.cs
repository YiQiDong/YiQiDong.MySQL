using Quick.Fields;
using System;
using System.Collections.Generic;
using YiQiDong.Protocol.V1.Model;
using YiQiDong.Core;
using YiQiDong.Agent;
using Quick.Shell.Utils;
using System.IO;
using YiQiDong.MySQL.Utils;

namespace YiQiDong.MySQL.Functions
{
    class PasswordManager : AbstractFunction
    {
        public override string Name => "密码管理";

        private List<FieldForGet> innerGet(FunctionRequest request)
        {
            List<FieldForGet> list = new List<FieldForGet>();
            string tmpKey;
            tmpKey = "password";
            list.Add(new FieldForGet()
            {
                Id = tmpKey,
                Name = "密码",
                Type = FieldType.InputText,
                Value = request == null ? Config.Instance.GetPassword() : request.GetFieldValue(tmpKey),
                Input_AllowBlank = false,
                Description = "root用户的密码"
            });
            return list;
        }

        public override FieldForGet[] Get()
        {
            //当容器未启动时，此功能不可用
            if (!AgentContext.Container.AutoStart)
            {
                return new FieldForGet[]
                {
                    new FieldForGet() { Name = "当前功能不可用", Description = $"容器尚未启动，当前功能不可用。", Input_ReadOnly = true, Type = FieldType.Alert }
                };
            }

            var list = innerGet(null);
            addSaveButton(list);
            return list.ToArray();
        }

        public override FieldForGet[] Post(FunctionRequest request)
        {
            var list = innerGet(request);
            if (request.IsFieldIdsMatch("Save"))
            {
                var oldPassword = Config.Instance.GetPassword();
                var newPassword = request.GetFieldValue("password");

                //先连接数据库修改密码
                MySqlUtils.ModifyPassword(
                    AgentContext.Container.ImageFolder,
                    Config.Instance.GetDataFolder(),
                    "root",
                    oldPassword,
                    newPassword);

                //然后再保存到配置文件
                try
                {
                    Config.Instance.UpdatePassword(newPassword);
                    list.Add(new FieldForGet()
                    {
                        Name = "修改成功",
                        Description = $"修改root账号密码成功！",
                        Type = FieldType.MessageBox
                    });
                }
                catch (Exception ex)
                {
                    list.Add(new FieldForGet()
                    {
                        Name = "错误",
                        Description = ex.Message,
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
            list.Add(new FieldForGet() { Id = "Save", Name = "修改", Type = FieldType.Button });
        }
    }
}
