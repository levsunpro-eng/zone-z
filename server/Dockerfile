FROM python:3.12-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app
COPY lobby_server.py /app/lobby_server.py
USER 10001:10001
EXPOSE 7780/tcp
ENTRYPOINT ["python", "/app/lobby_server.py", "--host", "0.0.0.0"]
CMD ["--cert", "/run/tls/fullchain.pem", "--key", "/run/tls/privkey.pem"]
