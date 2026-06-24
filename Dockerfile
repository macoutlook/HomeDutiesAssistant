# Builds the Blazor Server web front-end into a self-contained runtime image.
# Multi-stage: the SDK image compiles and publishes; the smaller aspnet image
# actually runs. The data/*.yaml facts live in the Core project and flow into
# the publish output via the project reference, so the knowledge base ships
# inside the image — no volume mount needed for it.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the sources and publish in one step (restore happens implicitly). We do
# NOT split restore + `publish --no-restore`: a separately-cached restore leaves
# the framework's static-web-asset processing incomplete, so _framework/
# blazor.web.js never gets emitted — which silently breaks Blazor Server
# interactivity (the page renders but never goes live). The web app only needs
# the Core library, so the console project isn't copied.
COPY HomeDutiesAssistant.Core/ HomeDutiesAssistant.Core/
COPY HomeDutiesAssistant.Web/ HomeDutiesAssistant.Web/
RUN dotnet publish HomeDutiesAssistant.Web/HomeDutiesAssistant.Web.csproj -c Release -o /app

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Listen on all interfaces so the container port can be published to the LAN.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "HomeDutiesAssistant.Web.dll"]