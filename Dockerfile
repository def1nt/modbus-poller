FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build-env

WORKDIR /app
COPY *.csproj .
COPY *.cs .
RUN dotnet restore
RUN dotnet publish -c Release --sc -r linux-x64

FROM mcr.microsoft.com/dotnet/runtime-deps:7.0
EXPOSE 8899
WORKDIR /app
COPY *.json .
COPY --from=build-env ./bin/Release/net7.0/linux-x64/ .
ENTRYPOINT ["./usr-device-test"]
