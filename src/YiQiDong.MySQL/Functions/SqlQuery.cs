using System;
using System.Collections.Generic;
using System.Text;
using YiQiDong.Protocol.V1.Model;
using System.Linq;
using System.Collections;
using Quick.Fields;
using MySql.Data.MySqlClient;
using YiQiDong.Core;

namespace YiQiDong.MySQL.Functions
{
    public class SqlQuery : AbstractFunction
    {
        public override string Name => "SQL查询";

        private List<FieldForGet> innerGet(FunctionRequest request)
        {
            List<FieldForGet> list = new List<FieldForGet>();

            List<FieldForGet> spliterFieldChildren = new List<FieldForGet>();
            List<FieldForGet> connectionFieldChildren = new List<FieldForGet>();

            connectionFieldChildren.Add(new FieldForGet()
            {
                Id = "ConnectTo",
                Name = "连接到",
                Type = FieldType.InputSelect,
                Input_AllowBlank = false,
                PostOnChanged = true,
                InputSelect_Options = new Dictionary<string, string>()
                {
                    ["Self"] = "当前容器",
                    ["Other"] = "其他服务"
                },
                Value = request == null ? "Self" : request.GetFieldValue("tab", "ConnectionInfo", "ConnectTo")
            });
            connectionFieldChildren.Add(new FieldForGet()
            {
                Id = "Charset",
                Name = "编码",
                Type = FieldType.InputSelect,
                Input_AllowBlank = false,
                InputSelect_Options = new Dictionary<string, string>()
                {
                    ["utf8"] = "utf8",
                    ["gb2312"] = "gb2312",
                    ["utf8mb4"] = "utf8mb4"
                },
                Value = request == null ? "utf8" : request.GetFieldValue("tab", "ConnectionInfo", "Charset")
            });
            if (request != null && request.GetFieldValue("tab", "ConnectionInfo", "ConnectTo") != "Self")
            {
                connectionFieldChildren.Add(new FieldForGet()
                {
                    Id = "Host",
                    Name = "主机",
                    Type = FieldType.InputText,
                    Input_AllowBlank = false,
                    Value = request == null ? "127.0.0.1" : request.GetFieldValue("tab", "ConnectionInfo", "Host")
                });
                connectionFieldChildren.Add(new FieldForGet()
                {
                    Id = "Port",
                    Name = "端口",
                    Type = FieldType.InputNumber,
                    Input_AllowBlank = false,
                    Input_RegularExpression = "^[1-9]$|(^[1-9][0-9]$)|(^[1-9][0-9][0-9]$)|(^[1-9][0-9][0-9][0-9]$)|(^[1-6][0-5][0-5][0-3][0-5]$)",
                    Value = request == null ? "9043" : request.GetFieldValue("tab", "ConnectionInfo", "Port")
                });
                connectionFieldChildren.Add(new FieldForGet()
                {
                    Id = "User",
                    Name = "用户",
                    Type = FieldType.InputText,
                    Input_AllowBlank = true,
                    Value = request == null ? null : request.GetFieldValue("tab", "ConnectionInfo", "User")
                });
                connectionFieldChildren.Add(new FieldForGet()
                {
                    Id = "Password",
                    Name = "密码",
                    Type = FieldType.InputText,
                    Input_AllowBlank = true,
                    Value = request == null ? null : request.GetFieldValue("tab", "ConnectionInfo", "Password")
                });
            }
            spliterFieldChildren.Add(new FieldForGet()
            {
                Id = "ConnectionInfo",
                Name = "连接信息",
                Type = FieldType.ContainerGroup,
                Children = connectionFieldChildren.ToArray()
            });

            spliterFieldChildren.Add(new FieldForGet()
            {
                Id = "Query",
                Name = "查询",
                Type = FieldType.ContainerGroup,
                Children = new FieldForGet[]
                {
                    new FieldForGet()
                    {
                        Id = "Script",
                        Name = "脚本",
                        Type = FieldType.InputTextArea,
                        InputTextArea_Rows = 8,
                        Input_AllowBlank = true,
                        Value = request == null ? null : request.GetFieldValue("tab", "Query","Script")
                    },
                    new FieldForGet() { Id = "Execute", Name = "执行", Type = FieldType.Button }
                }
            });

            list.Add(new FieldForGet()
            {
                Id = "tab",
                Type = FieldType.ContainerTab,
                Children = spliterFieldChildren.ToArray()
            });
            return list;
        }

        public override FieldForGet[] Get()
        {
            return innerGet(null).ToArray();
        }

        public override FieldForGet[] Post(FunctionRequest request)
        {
            var list = innerGet(request);

            if (request.IsFieldIdsMatch("tab", "Query", "Execute"))
            {
                var script = request.GetFieldValue("tab", "Query", "Script");
                var charSet = request.GetFieldValue("tab", "ConnectionInfo", "Charset");
                if (string.IsNullOrEmpty(script))
                {
                    list.Add(new FieldForGet() { Name = "错误", Description = "未输入要执行的脚本。", Type = FieldType.MessageBox });
                    return list.ToArray();
                }
                string host = null;
                int port = 0;
                string user = null;
                string password = null;

                switch (request.GetFieldValue("tab", "ConnectionInfo", "ConnectTo"))
                {
                    case "Self":
                        host = Config.Instance.GetConnectHost();
                        port = Config.Instance.GetConnectPort();
                        user = "root";
                        password = PasswordManager.Instance.Properties["password"];
                        break;
                    case "Other":
                        host = request.GetFieldValue("tab", "ConnectionInfo", "Host");
                        port = int.Parse(request.GetFieldValue("tab", "ConnectionInfo", "Port"));
                        user = request.GetFieldValue("tab", "ConnectionInfo", "User");
                        password = request.GetFieldValue("tab", "ConnectionInfo", "Password");
                        break;
                }

                try
                {
                    var connectionString = $"Server={host};Port={port};Database=mysql;Uid={user};Pwd={password};CharSet={charSet};";
                    //先连接数据库修改密码
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();
                        StringBuilder sb = new StringBuilder();
                        using (var cmd = new MySqlCommand(script, connection))
                        {
                            var reader = cmd.ExecuteReader();
                            var resultIndex = 1;
                            do
                            {
                                if (resultIndex > 1)
                                    sb.AppendLine();
                                sb.AppendLine("结果" + resultIndex);
                                resultIndex++;
                                if (reader.FieldCount > 0)
                                {
                                    List<string> columnList = new List<string>();
                                    for (var i = 0; i < reader.FieldCount; i++)
                                        columnList.Add(reader.GetName(i));
                                    var columnLine = string.Join(" | ", columnList);
                                    sb.AppendLine(string.Empty.PadRight(columnLine.Length, '-'));
                                    sb.AppendLine(columnLine);
                                    sb.AppendLine(string.Empty.PadRight(columnLine.Length, '-'));

                                    while (reader.Read())
                                    {
                                        var isFirstCell = true;
                                        for (var i = 0; i < reader.FieldCount; i++)
                                        {
                                            if (!isFirstCell)
                                                sb.Append(" | ");
                                            isFirstCell = false;
                                            sb.Append(reader.GetValue(i).ToString());
                                        }
                                        sb.AppendLine();
                                    }
                                }
                                else
                                {
                                    sb.AppendLine($"影响的记录数：{reader.RecordsAffected}");
                                }
                            } while (reader.NextResult());
                        }
                        list.Add(new FieldForGet() { Name = "查询结果", Description = sb.ToString(), Type = FieldType.Alert });
                    }
                }
                catch (Exception ex)
                {
                    list.Add(new FieldForGet() { Name = "错误", Description = ex.Message, Input_ReadOnly = true, Type = FieldType.Alert });
                }
            }
            return list.ToArray();
        }
    }
}
