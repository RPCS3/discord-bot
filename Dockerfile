FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS base

# Native libgdiplus dependencies
RUN apt-get update
RUN DEBIAN_FRONTEND=noninteractive TZ=Etc/UTC apt-get install -y --allow-unauthenticated libc6-dev libgdiplus libx11-dev fonts-roboto tzdata libarchive13t64 liblept5
RUN wget https://archive.ubuntu.com/ubuntu/pool/main/t/tiff/libtiff5_4.3.0-6ubuntu0.12_amd64.deb
RUN dpkg -i ./libtiff5_4.3.0-6ubuntu0.12_amd64.deb
RUN rm ./libtiff5_4.3.0-6ubuntu0.12_amd64.deb

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
