FROM python:3.7-slim-stretch

RUN apt-get update && apt-get install -y libssl1.0-dev
ENV WEBSOCKET_ADDR http://+:4288/
ENV ALLOW_PYTHON_EXECUTABLE /usr/local/bin/python
ADD output/bin/Debug/linux-x64/publish /usr/local/python-language-server
RUN chmod +x /usr/local/python-language-server/Microsoft.Python.LanguageServer
USER nobody
ENTRYPOINT ["/usr/local/python-language-server/Microsoft.Python.LanguageServer"]