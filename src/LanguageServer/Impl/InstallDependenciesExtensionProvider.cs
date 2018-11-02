// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Python.LanguageServer;
using Microsoft.Python.LanguageServer.Extensions;
using Microsoft.Python.LanguageServer.Implementation;
using Microsoft.PythonTools;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Interpreter.Ast;
using Microsoft.PythonTools.Parsing.Ast;

namespace InstallDependenciesExtension {
        class InstallDependenciesExtensionProvider : ILanguageServerExtensionProvider {
            public Task<ILanguageServerExtension> CreateAsync(IPythonLanguageServer server, IReadOnlyDictionary<string, object> properties, CancellationToken cancellationToken) {
                Console.Error.WriteLine("### Hello from extension");
                if (server is Server s) {
                s.OnCommand += (object sender, CommandEventArgs e) => {
                    Console.Error.WriteLine("### COMMAND");
                };
                } else {
                    Console.Error.WriteLine("### NOT PYTHON");
                }
                return Task.FromResult<ILanguageServerExtension>(new GetAllExtension((Server)server, properties));
            }

            private class GetAllExtension : ILanguageServerExtension {
                private readonly BuiltinTypeId _typeId;
                private readonly Server _server;

                public GetAllExtension(Server server, IReadOnlyDictionary<string, object> properties) {
                    _server = server;
                    if (!Enum.TryParse((string)properties["typeid"], out _typeId)) {
                        throw new ArgumentException("typeid was not valid");
                    }
                }

                public string Name => "getall";

                public void Dispose() { }

                public Task<IReadOnlyDictionary<string, object>> ExecuteCommand(string command, IReadOnlyDictionary<string, object> properties, CancellationToken token) {
                    Console.Error.WriteLine("ExecuteCommand!");
                    if (properties == null) {
                        return null;
                    }

                    // Very bad code, but good for testing. Copy/paste at your own risk!
                    var entry = _server.GetEntry(new Uri((string)properties["uri"])) as IPythonProjectEntry;
                    var location = new SourceLocation((int)properties["line"], (int)properties["column"]);

                    if (command == _typeId.ToString()) {
                        var res = new List<string>();
                        foreach (var m in entry.Analysis.GetAllMembers(location)) {
                            if (m.Values.Any(v => v.MemberType == PythonMemberType.Constant && v.TypeId == _typeId)) {
                                res.Add(m.Name);
                            }
                        }
                        return Task.FromResult<IReadOnlyDictionary<string, object>>(new Dictionary<string, object> { ["names"] = res });
                    }
                    return Task.FromResult<IReadOnlyDictionary<string, object>>(null);
                }

                public Task Initialize(IServiceContainer services, CancellationToken token) => Task.CompletedTask;
            }
        }
}