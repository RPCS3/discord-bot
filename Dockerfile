FROM mcr.microsoft.com/dotnet/core/sdk:2.1 AS base
COPY packages /root/.nuget/packages/
WORKDIR /src
COPY . .
RUN rm -rf ./packages
RUN git status
RUN dotnet build "CompatBot/CompatBot.csproj" -c Release
ENV RUNNING_IN_DOCKER true
WORKDIR /src/CompatBot
RUN dotnet run -c Release --dry-run
ENTRYPOINT ["dotnet", "run", "-c Release", "CompatBot.csproj"]