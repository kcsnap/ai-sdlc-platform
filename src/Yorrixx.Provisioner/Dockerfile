FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Platform retargets the provisioner to net8 (ADR-XR-0001); SDK + runtime images must match, or the
# net8 app won't start on a net9 runtime. (global.json is absent in this repo — nothing to copy.)
COPY ["Directory.Build.props", "./"]
COPY . .
RUN dotnet publish "src/Yorrixx.Provisioner/Yorrixx.Provisioner.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Yorrixx.Provisioner.dll"]
