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

// #define WAIT_FOR_DEBUGGER

using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Microsoft.Python.LanguageServer.Services;
using Microsoft.PythonTools.Analysis.Infrastructure;
using Microsoft.PythonTools.LanguageServer.Services;
using Newtonsoft.Json;
using StreamJsonRpc;
using InstallDependenciesExtension;

using System.Collections.Generic;
using Microsoft.Python.LanguageServer;
using Microsoft.Python.LanguageServer.Extensions;
using Microsoft.Python.LanguageServer.Implementation;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Interpreter.Ast;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.Python.LanguageServer.Server {
    internal static class Program {
        public static void Main(string[] args) {
            CheckDebugMode();

            var useHTTP = true;
            if (useHTTP) {
                Console.Error.WriteLine("# Listening");
                HttpListener httpListener = new HttpListener();
                httpListener.Prefixes.Add("http://localhost:4288/");
                httpListener.Start();
                ListenWebSocketAsync(httpListener).GetAwaiter().GetResult();
            } else {
                using (var cin = Console.OpenStandardInput())
                using (var cout = Console.OpenStandardOutput()) {
                    HandleConnectionAsync(new JsonRpc(cout, cin)).GetAwaiter().GetResult();
                }
            }
        }

        private static async System.Threading.Tasks.Task ListenWebSocketAsync(HttpListener httpListener) {
            while (true) {
                HttpListenerContext context = await httpListener.GetContextAsync();
                Console.Error.WriteLine("# HTTP connected");
                if (context.Request.IsWebSocketRequest) {
                    HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
                    WebSocket webSocket = webSocketContext.WebSocket;
                    if (webSocket.State == WebSocketState.Open) {
                        HandleWebSocketConnectionAsync(webSocket);
                    }
                } else {
                    context.Response.StatusCode = 426; // HTTP 426 Upgrade Required
                    context.Response.Close();
                }
            }
        }

        private async static System.Threading.Tasks.Task HandleWebSocketConnectionAsync(WebSocket webSocket) {
            Console.Error.WriteLine("# WebSocket connected");
            using (var handler = new WebSocketMessageHandler(webSocket)) {
                using (JsonRpc rpc = new JsonRpc(handler)) {
                    await HandleConnectionAsync(rpc);
                }
            }
            Console.Error.WriteLine("# WebSocket disconnected");
        }

        private async static System.Threading.Tasks.Task HandleConnectionAsync(JsonRpc rpc) {
            using (var server = new Implementation.LanguageServer()) {
                await server._server.LoadExtensionAsync(new PythonAnalysisExtensionParams {
                    assembly = typeof(InstallDependenciesExtensionProvider).Assembly.FullName,
                    typeName = typeof(InstallDependenciesExtensionProvider).FullName,
                    properties = new Dictionary<string, object> { ["typeid"] = BuiltinTypeId.Int.ToString() }
                }, null, CancellationToken.None);

                rpc.AddLocalRpcTarget(server);
                rpc.SynchronizationContext = new SingleThreadSynchronizationContext();
                rpc.JsonSerializer.Converters.Add(new UriConverter());
                using (var services = new ServiceManager()) {
                    services.AddService(new UIService(rpc));
                    services.AddService(new ProgressService(rpc));
                    services.AddService(new TelemetryService(rpc));
                    var token = server.Start(services, rpc);
                    rpc.StartListening();
                    token.WaitHandle.WaitOne();
                }
            }
        }

        private static void CheckDebugMode() {
#if WAIT_FOR_DEBUGGER
            var start = DateTime.Now;
            while (!System.Diagnostics.Debugger.IsAttached) {
                System.Threading.Thread.Sleep(1000);
                if ((DateTime.Now - start).TotalMilliseconds > 15000) {
                    break;
                }
            }
#endif
        }
    }

    sealed class UriConverter : JsonConverter {
        public override bool CanConvert(Type objectType) => objectType == typeof(Uri);
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            if (reader.TokenType == JsonToken.String) {
                var str = (string)reader.Value;
                return new Uri(str.Replace("%3A", ":"));
            }

            if (reader.TokenType == JsonToken.Null) {
                return null;
            }

            throw new InvalidOperationException($"UriConverter: unsupported token type {reader.TokenType}");
        }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            if (null == value) {
                writer.WriteNull();
                return;
            }

            if (value is Uri) {
                var uri = (Uri)value;
                var scheme = uri.Scheme;
                var str = uri.ToString();
                str = uri.Scheme + "://" + str.Substring(scheme.Length + 3).Replace(":", "%3A").Replace('\\', '/');
                writer.WriteValue(str);
                return;
            }

            throw new InvalidOperationException($"UriConverter: unsupported value type {value.GetType()}");
        }
    }
}