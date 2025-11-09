# Stage 1: Build the backend
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /source
COPY server/. .
RUN dotnet publish --nologo -c Release -o /output -r linux-musl-x64 /p:PublishProfile=FolderProfile SecureMailingList.csproj

# Stage 2: Create the runtime image
FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine AS runtime
WORKDIR /app
COPY --from=build /output /app
COPY static_root/. /app/static_root
RUN adduser --disabled-password --home /app --gecos '' noprivileges && chown -R noprivileges /app && ls -al /app
USER noprivileges
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENTRYPOINT ["/app/SecureMailingList"]