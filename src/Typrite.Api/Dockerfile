FROM node:26-alpine AS admin
WORKDIR /admin
RUN npm install --global pnpm@11.17.0
COPY src/admin/package.json src/admin/pnpm-lock.yaml src/admin/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY src/admin/ ./
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Typrite.Api -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir -p /data/blobs && chown -R $APP_UID:$APP_UID /data/blobs
COPY --from=build /app .
COPY --from=admin /admin/dist ./wwwroot
ENV Storage__Root=/data/blobs
USER $APP_UID
ENTRYPOINT ["dotnet", "Typrite.Api.dll"]
