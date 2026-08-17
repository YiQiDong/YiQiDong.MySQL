using YiQiDong.Protocol.V1.Model;
using Quick.Fields;
using YiQiDong.Core;
using MySqlConnector;
using Quick.Utils;

namespace YiQiDong.MySQL.Functions
{
    public class SqlQuery : AbstractFunction
    {
        public override string Name => "SQL查询";

        private List<FieldForGet> innerGet(FunctionRequest request)
        {
            List<FieldForGet> list = new List<FieldForGet>();

            List<FieldForGet> spliterFieldChildren = new List<FieldForGet>();
            List<FieldForGet> connectionFieldChildren =
            [
                new FieldForGet()
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
                    Value = request == null ? "Self" : request.GetFieldValue("ConnectionInfo", "ConnectTo")
                },
                new FieldForGet()
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
                    Value = request == null ? "utf8" : request.GetFieldValue("ConnectionInfo", "Charset")
                },
            ];
            if (request != null && request.GetFieldValue("ConnectionInfo", "ConnectTo") != "Self")
            {
                connectionFieldChildren.Add(new FieldForGet()
                {
                    Id = "Host",
                    Name = "主机",
                    Type = FieldType.InputText,
                    Input_AllowBlank = false,
                    Value = request == null ? "127.0.0.1" : request.GetFieldValue("ConnectionInfo", "Host")
                });
                connectionFieldChildren.Add(new FieldForGet()
                {
                    Id = "Port",
                    Name = "端口",
                    Type = FieldType.InputNumber,
                    Input_AllowBlank = false,
                    Value = request == null ? "3306" : request.GetFieldValue("ConnectionInfo", "Port")
                });
                connectionFieldChildren.Add(new FieldForGet()
                {
                    Id = "User",
                    Name = "用户",
                    Type = FieldType.InputText,
                    Input_AllowBlank = true,
                    Value = request == null ? null : request.GetFieldValue("ConnectionInfo", "User")
                });
                connectionFieldChildren.Add(new FieldForGet()
                {
                    Id = "Password",
                    Name = "密码",
                    Type = FieldType.InputText,
                    Input_AllowBlank = true,
                    Value = request == null ? null : request.GetFieldValue("ConnectionInfo", "Password")
                });
            }
            spliterFieldChildren.Add(new FieldForGet()
            {
                Id = "ConnectionInfo",
                Name = "连接信息",
                Type = FieldType.ContainerGroup,
                Children = connectionFieldChildren
            });

            spliterFieldChildren.Add(new FieldForGet()
            {
                Id = "Query",
                Name = "查询",
                Type = FieldType.ContainerGroup,
                Children =
                [
                    new FieldForGet()
                    {
                        Id = "Script",
                        Name = "脚本",
                        Type = FieldType.InputTextArea,
                        InputTextArea_Rows = 8,
                        Input_AllowBlank = true,
                        Value = request == null ? null : request.GetFieldValue("Query","Script")
                    },
                    new FieldForGet() { Id = "Execute", Name = "执行",MarginBottom=3, Type = FieldType.Button }
                ]
            });

            list.Add(new FieldForGet()
            {
                Type = FieldType.ContainerTab,
                Children = spliterFieldChildren
            });
            return list;
        }

        public override List<FieldForGet> Execute(FunctionRequest request)
        {
            if(request==null)
                return Get();
            return Post(request);
        }

        private List<FieldForGet> Get()
        {
            return innerGet(null);
        }

        private List<FieldForGet> Post(FunctionRequest request)
        {
            var list = innerGet(request);

            if (request.IsFieldIdsMatch("Query", "Execute"))
            {
                var script = request.GetFieldValue("Query", "Script");
                var charSet = request.GetFieldValue("ConnectionInfo", "Charset");
                if (string.IsNullOrEmpty(script))
                {
                    list.Add(new FieldForGet() { Name = "错误", Description = "未输入要执行的脚本。", Type = FieldType.MessageBox });
                    return list;
                }
                string host = null;
                int port = 0;
                string user = null;
                string password = null;

                switch (request.GetFieldValue("ConnectionInfo", "ConnectTo"))
                {
                    case "Self":
                        host = Config.Instance.GetConnectHost();
                        port = Config.Instance.GetConnectPort();
                        user = "root";
                        password = Functions.Config.Instance.GetPassword();
                        break;
                    case "Other":
                        host = request.GetFieldValue("ConnectionInfo", "Host");
                        port = int.Parse(request.GetFieldValue("ConnectionInfo", "Port"));
                        user = request.GetFieldValue("ConnectionInfo", "User");
                        password = request.GetFieldValue("ConnectionInfo", "Password");
                        break;
                }

                try
                {
                    var connectionStringBuilder = new MySqlConnectionStringBuilder()
                    {
                        Server = host,
                        Port = Convert.ToUInt32(port),
                        Database = "mysql",
                        UserID = user,
                        Password = password,
                        CharacterSet = charSet,
                        SslMode = MySqlSslMode.None,
                        AllowPublicKeyRetrieval = true
                    };
                    //先连接数据库修改密码
                    using (var connection = new MySqlConnection(connectionStringBuilder.ConnectionString))
                    {
                        connection.Open();
                        using (var cmd = new MySqlCommand(script, connection))
                        {
                            var reader = cmd.ExecuteReader();
                            var readerIndex = 0;

                            var tabContainerField = new FieldForGet()
                            {
                                Type = FieldType.ContainerTab
                            };
                            var tabContainerFieldChildList = new List<FieldForGet>();
                            do
                            {
                                readerIndex++;
                                var tableField = new FieldForGet()
                                {
                                    Type = FieldType.ContainerTable,
                                    ContainerTable_Bordered = true,
                                    ContainerTable_Hoverable = true
                                };
                                List<FieldForGet> trFieldList = new List<FieldForGet>();
                                if (reader.FieldCount > 0)
                                {
                                    var headTr = new FieldForGet() { Type = FieldType.ContainerTableTr };
                                    var headTrChildList = new List<FieldForGet>();
                                    for (var i = 0; i < reader.FieldCount; i++)
                                    {
                                        headTrChildList.Add(new FieldForGet()
                                        {
                                            Type = FieldType.ContainerTableTh,
                                            Value = reader.GetName(i)
                                        });
                                    }
                                    headTr.Children = headTrChildList;
                                    trFieldList.Add(headTr);

                                    while (reader.Read())
                                    {
                                        var rowTr = new FieldForGet() { Type = FieldType.ContainerTableTr };
                                        var rowTrChildList = new List<FieldForGet>();

                                        for (var i = 0; i < reader.FieldCount; i++)
                                        {
                                            rowTrChildList.Add(new FieldForGet()
                                            {
                                                Type = FieldType.ContainerTableTd,
                                                Value = reader.GetValue(i)?.ToString()
                                            });
                                        }
                                        rowTr.Children = rowTrChildList;
                                        trFieldList.Add(rowTr);
                                    }
                                }
                                else
                                {
                                    trFieldList.Add(new FieldForGet()
                                    {
                                        Type = FieldType.ContainerTableTr,
                                        Value = "影响的记录数",
                                        Children = 
                                        [
                                            new FieldForGet(){ Type = FieldType.ContainerTableTd,Value = reader.RecordsAffected.ToString() }
                                        ]
                                    });
                                }
                                tableField.Children = trFieldList;
                                tabContainerFieldChildList.Add(new FieldForGet()
                                {
                                    Id = "result" + readerIndex,
                                    Type = FieldType.ContainerGroup,
                                    Name = "结果" + readerIndex,
                                    Children = [ tableField ]
                                });
                            } while (reader.NextResult());
                            tabContainerField.Children = tabContainerFieldChildList;
                            list.Add(tabContainerField);
                        }
                    }
                }
                catch (Exception ex)
                {
                    list.Add(new FieldForGet() { Name = "错误", Description = ExceptionUtils.GetExceptionString(ex), Input_ReadOnly = true, Type = FieldType.Alert });
                }
            }
            return list;
        }
    }
}
