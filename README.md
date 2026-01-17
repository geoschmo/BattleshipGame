# Battleship Game

A standalone multiplayer Battleship game built with ASP.NET Core 8.0 and SignalR.

## Features

- **Multiplayer Mode**: Real-time two-player games with room codes
- **Single Player Mode**: Play against AI with Hunt/Target algorithm
- **Game Recovery**: Reconnect to interrupted games within 5 minutes
- **Multiple Board Sizes**: Large (10x10), Medium (9x9), Small (8x8)
- **Responsive Design**: Works on desktop and mobile

## Local Development

```bash
# Run the application
dotnet run --project BattleshipGame/BattleshipGame.csproj

# Run with hot reload
dotnet watch --project BattleshipGame/BattleshipGame.csproj
```

The game will be available at `https://localhost:5001` or `http://localhost:5000`.

## Deployment to SmarterASP.NET

### Step 1: Create a New Site in SmarterASP Control Panel

1. Log into your SmarterASP.NET control panel
2. Go to **Websites** > **Add New Website**
3. Give it a name (e.g., "Battleship")
4. Note the temporary URL (e.g., `geoschmo-001-site3.ntempurl.com`)

### Step 2: Publish the Application

```bash
# Publish for deployment
dotnet publish BattleshipGame/BattleshipGame.csproj --configuration Release --output ./publish
```

### Step 3: Deploy via Web Deploy or FTP

**Option A: Web Deploy (Recommended)**

1. In SmarterASP control panel, go to **Websites** > select your site > **Publish Settings**
2. Download the publish profile (.PublishSettings file)
3. Use Visual Studio or `msdeploy` to deploy

**Option B: FTP Upload**

1. Get FTP credentials from SmarterASP control panel
2. Upload the contents of the `./publish` folder to the site's root directory
3. Ensure `web.config` is included (generated during publish)

### Step 4: Configure the Portfolio Link

In your portfolio site's `appsettings.json`, update the Battleship URL:

```json
{
  "BattleshipGameUrl": "https://geoschmo-001-site3.ntempurl.com"
}
```

### Step 5: Configure Back Link (Optional)

In the Battleship site's `appsettings.json`, add a link back to your portfolio:

```json
{
  "PortfolioUrl": "https://geoschmo-001-site2.ntempurl.com"
}
```

## GitHub Actions CI/CD (Optional)

To set up automated deployment, create `.github/workflows/deploy.yml`:

```yaml
name: Deploy Battleship Game

on:
  push:
    branches: [ master, main ]

jobs:
  deploy:
    runs-on: windows-latest
    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --configuration Release --no-restore

    - name: Publish
      run: dotnet publish BattleshipGame/BattleshipGame.csproj --configuration Release --output ./publish --no-build

    - name: Deploy to SmarterASP
      uses: talunzhang/auto-web-deploy@v1.0.1
      with:
        website-name: ${{ secrets.BATTLESHIP_WEBSITE_NAME }}
        server-computer-name: ${{ secrets.SERVER_COMPUTER_NAME }}
        server-username: ${{ secrets.SERVER_USERNAME }}
        server-password: ${{ secrets.SERVER_PASSWORD }}
        source-path: './publish'
```

Required GitHub Secrets:
- `BATTLESHIP_WEBSITE_NAME` - Your SmarterASP site name for Battleship
- `SERVER_COMPUTER_NAME` - SmarterASP server (from publish profile)
- `SERVER_USERNAME` - Your deployment username
- `SERVER_PASSWORD` - Your deployment password

## Architecture Notes

### SignalR Hub

The game uses SignalR for real-time communication. The hub is mapped at `/gameHub`.

### Data Persistence

Game state is stored using LiteDB. The database file is created in one of these locations (in order of preference):
1. `App_Data/battleship.db` (in the application directory)
2. `%LOCALAPPDATA%/BattleshipGame/battleship.db`
3. Temp directory
4. In-memory (if all else fails)

### Single Player AI

The AI uses a Hunt/Target algorithm:
- **Hunt Mode**: Random attacks in a checkerboard pattern
- **Target Mode**: After a hit, attacks adjacent cells to find the rest of the ship

## Troubleshooting

### SignalR Connection Issues

If SignalR fails to connect:
1. Ensure WebSockets are enabled in your hosting panel
2. Check that the `/gameHub` endpoint is accessible
3. Verify CORS settings if accessing from a different domain

### Database Permission Errors

If you see database errors:
1. Ensure the `App_Data` folder exists and is writable
2. The app will fall back to in-memory mode if file persistence fails
3. Check SmarterASP file permissions in the control panel
