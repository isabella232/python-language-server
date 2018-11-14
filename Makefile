.PHONY: docker output/bin/Debug/linux-x64/publish/Microsoft.Python.LanguageServer

run:
	cd src/LanguageServer/Impl && dotnet run linux-x64 Microsoft.Python.LanguageServer.csproj -- --debug

output/bin/Debug/linux-x64/publish/Microsoft.Python.LanguageServer:
	cd src/LanguageServer/Impl && dotnet publish -r linux-x64 Microsoft.Python.LanguageServer.csproj

docker: output/bin/Debug/linux-x64/publish/Microsoft.Python.LanguageServer
	docker build -t sourcegraph/python-language-server .

run-docker:
	docker run -p 4288:4288 sourcegraph/python-language-server