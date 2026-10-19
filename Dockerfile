FROM mcr.microsoft.com/dotnet/sdk:10.0-resolute AS base

# Native libgdiplus dependencies
RUN apt-get update
RUN DEBIAN_FRONTEND=noninteractive TZ=Etc/UTC apt-get install -y --allow-unauthenticated libc6-dev libgdiplus libx11-dev fonts-roboto tzdata libarchive13t64 libleptonica6
RUN ln -s /usr/lib/x86_64-linux-gnu/libleptonica.so.6 /usr/lib/x86_64-linux-gnu/liblept.so.5

# Regular stuff
#COPY packages /root/.nuget/packages/
WORKDIR /src
COPY . .
#RUN rm -rf ./packages
RUN git status
# Asks for user/pw otherwise..
RUN git remote set-url origin https://github.com/RPCS3/discord-bot.git
RUN git config --remove-section http."https://github.com/"
# Build and test everything
RUN dotnet restore "CompatBot/CompatBot.csproj"
RUN dotnet build "CompatBot/CompatBot.csproj" -c Release
ENV RUNNING_IN_DOCKER true
# Limit server GC to 512 MB heap max
ENV COMPlus_gcServer 1
# ENV COMPlus_GCHeapHardLimit 0x20000000
WORKDIR /src/CompatBot
RUN dotnet run -c Release --dry-run
ENTRYPOINT ["dotnet", "run", "-c", "Release", "CompatBot.csproj"]
#ENTRYPOINT ["/bin/bash"]
