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
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
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

namespace WorkspaceExtractArchive {
    /// <summary>
    /// Registers a command workspace/extractArchive that extracts a Zip archive from a URL to the workspace root,
    /// to support analysis of workspaces that were not originally on the local file system.
    /// </summary>
    class WorkspaceExtractArchiveExtensionProvider : ILanguageServerExtensionProvider {
        public Task<ILanguageServerExtension> CreateAsync(IPythonLanguageServer server, IReadOnlyDictionary<string, object> properties, CancellationToken cancellationToken) {
            return Task.FromResult<ILanguageServerExtension>(new WorkspaceExtractArchive((Server)server, properties));
        }

        private class WorkspaceExtractArchive : ILanguageServerExtension {
            private readonly Server _server;

            public WorkspaceExtractArchive(Server server, IReadOnlyDictionary<string, object> properties) {
                _server = server;
            }

            public string Name => "WorkspaceExtractArchive";

            public Task<IReadOnlyDictionary<string, object>> ExecuteCommand(string command, IReadOnlyDictionary<string, object> properties, CancellationToken token) {
                return Task.FromResult<IReadOnlyDictionary<string, object>>(null);
            }

            public Task Initialize(IServiceContainer services, CancellationToken token) {
                if (_server is Server s) {
                    s.OnCommand += Run;
                }
                return Task.CompletedTask;
            }

            private void Run(object sender, CommandEventArgs e) {
                if (e.command == "workspace/extractArchive") {
                    Uri rootUri = _server.GetInitializeResult().rootUri;
                    if (rootUri == null) {
                        throw new InvalidOperationException("unable to extract archive because the workspace has no rootUri");
                    }
                    if (e.arguments.Length != 2) {
                        throw new InvalidOperationException($"invalid arguments (expected exactly 2, got {e.arguments.Length}");
                    }
                    // Arguments are [zip archive URL, number of leading path components to strip]. The 2nd
                    // argument is useful when the Zip archive contains a top-level directory that all files are
                    // underneath, such as with GitHub's codeload (which contains a directory named
                    // ${REPO}-${ABBREV_REF}/.)
                    //
                    // The stripLeadingPathComponent support is useful because it lets you use this
                    // extension with archives created by GitHub not Sourcegraph (which do not have a leading
                    // prefix dir). This may let you use it for private code.
                    if (!(e.arguments[0] is string)) {
                        throw new InvalidOperationException("invalid 1st argument (expected string)");
                    }
                    if (!(e.arguments[1] is bool)) {
                        throw new InvalidOperationException("invalid 2nd argument (expected boolean)");
                    }

                    var archiveUrl = (string)e.arguments[0];
                    var stripLeadingPathComponent = (bool)e.arguments[1];
                    DownloadAndExtractArchive(archiveUrl, stripLeadingPathComponent, rootUri.ToAbsolutePath());
                }
            }

            private void DownloadAndExtractArchive(string archiveUrl, bool stripLeadingPathComponent, string extractPath) {
                // Normalizes the path and ensure it ends with the directory separator so we can check that all extraction paths are underneath it.
                extractPath = Path.GetFullPath(extractPath);
                if (!extractPath.EndsWith(Path.DirectorySeparatorChar)) {
                    extractPath += Path.DirectorySeparatorChar;
                }

                _server.LogMessage(MessageType.Info, $"Downloading and extracting archive {archiveUrl} to {extractPath}");
                using (var client = new WebClient()) {
                    client.Headers["Accept"] = "application/zip";

                    // TODO(sqs): Assumes the userinfo contains *only* a username.
                    var username = new Uri(archiveUrl).UserInfo;
                    var encodedAuth = System.Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(username + ":"));
                    client.Headers["Authorization"] = $"Basic {encodedAuth}";

                    var content = client.DownloadData(archiveUrl);
                    using (var stream = new MemoryStream(content))
                    using (ZipArchive zipArchive = new ZipArchive(stream)) {
                        _server.LogMessage(MessageType.Info, $"Downloaded archive {archiveUrl}");
                        if (stripLeadingPathComponent) {
                            foreach (ZipArchiveEntry entry in zipArchive.Entries) {
                                if (entry.FullName.EndsWith(Path.DirectorySeparatorChar.ToString())) {
                                    continue;
                                }
                                var pathComponents = entry.FullName.Split(PathUtils.DirectorySeparators);
                                if (pathComponents.Length > 1) {
                                    var destinationPath = string.Join(Path.DirectorySeparatorChar.ToString(), pathComponents.Skip(1));
                                    destinationPath = Path.GetFullPath(Path.Combine(extractPath, destinationPath));

                                    // Prevent extracting to unsafe paths that are outside of the extractPath.
                                    if (destinationPath.StartsWith(extractPath, StringComparison.Ordinal)) {
                                        Directory.CreateDirectory(PathUtils.GetParent(destinationPath));
                                        entry.ExtractToFile(destinationPath);
                                    }
                                }
                            }
                        } else {
                            zipArchive.ExtractToDirectory(extractPath);
                        }
                        _server.LogMessage(MessageType.Info, $"Extracted archive {archiveUrl} to {extractPath}");
                    }
                }
            }

            public void Dispose() {
                if (_server is Server s) {
                    s.OnCommand -= Run;
                }
            }
        }
    }
}