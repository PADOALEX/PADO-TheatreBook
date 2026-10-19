FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PADO.TheatreBook.Api/PADO.TheatreBook.Api.csproj src/PADO.TheatreBook.Api/
RUN dotnet restore src/PADO.TheatreBook.Api/PADO.TheatreBook.Api.csproj
COPY src/ src/
RUN dotnet publish src/PADO.TheatreBook.Api/PADO.TheatreBook.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "PADO.TheatreBook.Api.dll"]
