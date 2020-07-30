FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS base
# Native libgdiplus dependencies
RUN apt-get update
RUN apt-get install -y --allow-unauthenticated libc6-dev libgdiplus libx11-dev fonts-roboto
RUN rm -rf /var/lib/apt/lists/*
RUN ln -s /lib/x86_64-linux-gnu/libdl-2.24.so /lib/x86_64-linux-gnu/libdl.so
# Regular stuff
COPY packages /root/.nuget/packages/
WORKDIR /src
COPY . .
RUN rm -rf ./packages
RUN git status
# Build and test everything
RUN dotnet build "CompatBot/CompatBot.csproj" -c Release
ENV RUNNING_IN_DOCKER true
# Limit server GC to 384 MB heap max
ENV COMPlus_gcServer 1
ENV COMPlus_GCHeapHardLimit 0x18000000
WORKDIR /src/CompatBot
RUN dotnet run -c Release --dry-run
ENTRYPOINT ["dotnet", "run", "-c", "Release", "CompatBot.csproj"]
