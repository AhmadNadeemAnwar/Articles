FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Articles.slnx ./
COPY src ./src
RUN dotnet publish src/Articles.Api/Articles.Api.csproj -c Release -o /app/publish --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
# Render injects PORT; Program.cs binds to it. 8080 is the fallback for running the image locally.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Articles.Api.dll"]
