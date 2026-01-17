// Game state
let connection = null;
let currentRoomCode = null;
let myConnectionId = null;
let isMyTurn = false;
let setupBoard = [];
let placedShips = new Set();
let currentShipType = 'Battleship';
let isHorizontal = false;

// Player token for game recovery
const PLAYER_TOKEN_KEY = 'battleship_player_token';
const ACTIVE_GAME_KEY = 'battleship_active_game';

function generateUUID() {
    // Use crypto.randomUUID if available (requires secure context)
    if (typeof crypto !== 'undefined' && crypto.randomUUID) {
        return crypto.randomUUID().replace(/-/g, '');
    }
    // Fallback for non-secure contexts or older browsers
    return 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx'.replace(/x/g, function() {
        return Math.floor(Math.random() * 16).toString(16);
    });
}

function getOrCreatePlayerToken() {
    try {
        let token = localStorage.getItem(PLAYER_TOKEN_KEY);
        if (!token) {
            token = generateUUID();
            localStorage.setItem(PLAYER_TOKEN_KEY, token);
        }
        return token;
    } catch {
        // localStorage might be blocked, generate a session-only token
        return generateUUID();
    }
}

function getPlayerToken() {
    try {
        return localStorage.getItem(PLAYER_TOKEN_KEY);
    } catch {
        return null;
    }
}

function saveActiveGame(roomCode) {
    try {
        localStorage.setItem(ACTIVE_GAME_KEY, roomCode);
    } catch {
        // localStorage blocked, ignore
    }
}

function clearActiveGame() {
    try {
        localStorage.removeItem(ACTIVE_GAME_KEY);
    } catch {
        // localStorage blocked, ignore
    }
}

// Game configuration (set when room is created/joined)
let gameConfig = {
    size: 'Large',
    boardSize: 10,
    shipTypes: ['Carrier', 'Battleship', 'Cruiser', 'Submarine', 'Destroyer']
};

const shipSizes = {
    'Carrier': 5,
    'Battleship': 4,
    'Cruiser': 3,
    'Submarine': 3,
    'Destroyer': 2
};

function initializeSetupBoardArray() {
    setupBoard = Array(gameConfig.boardSize).fill(null).map(() => Array(gameConfig.boardSize).fill(null));
}

// Single Player Mode
let isSinglePlayer = false;
let playerBoard = [];  // Player's ships for AI to attack

const BattleshipAI = {
    board: [],              // AI's ship placements
    ships: [],              // AI's ship objects with hit tracking
    attackHistory: new Set(), // Cells already attacked (as "row,col" strings)
    targetQueue: [],        // Adjacent cells to attack after a hit
    lastHit: null,          // Last successful hit for targeting

    reset() {
        this.board = [];
        this.ships = [];
        this.attackHistory = new Set();
        this.targetQueue = [];
        this.lastHit = null;
    },

    placeShips() {
        this.board = Array(gameConfig.boardSize).fill(null).map(() => Array(gameConfig.boardSize).fill(null));
        this.ships = [];

        for (const shipType of gameConfig.shipTypes) {
            let placed = false;
            let attempts = 0;

            while (!placed && attempts < 100) {
                const row = Math.floor(Math.random() * gameConfig.boardSize);
                const col = Math.floor(Math.random() * gameConfig.boardSize);
                const horizontal = Math.random() < 0.5;

                if (this.canPlaceShip(row, col, shipType, horizontal)) {
                    this.placeShip(row, col, shipType, horizontal);
                    placed = true;
                }
                attempts++;
            }

            if (!placed) {
                // Start over if placement failed
                return this.placeShips();
            }
        }
    },

    canPlaceShip(row, col, shipType, horizontal) {
        const size = shipSizes[shipType];
        for (let i = 0; i < size; i++) {
            const r = horizontal ? row : row + i;
            const c = horizontal ? col + i : col;
            if (r >= gameConfig.boardSize || c >= gameConfig.boardSize) return false;
            if (this.board[r][c] !== null) return false;
        }
        return true;
    },

    placeShip(row, col, shipType, horizontal) {
        const size = shipSizes[shipType];
        const coords = [];

        for (let i = 0; i < size; i++) {
            const r = horizontal ? row : row + i;
            const c = horizontal ? col + i : col;
            this.board[r][c] = shipType;
            coords.push({ row: r, col: c });
        }

        this.ships.push({
            type: shipType,
            size: size,
            coords: coords,
            hits: new Set(),
            isSunk() { return this.hits.size === this.size; }
        });
    },

    processPlayerAttack(row, col) {
        const shipType = this.board[row][col];
        if (shipType) {
            const ship = this.ships.find(s => s.type === shipType);
            ship.hits.add(`${row},${col}`);
            return { hit: true, sunk: ship.isSunk(), shipType: shipType };
        }
        return { hit: false, sunk: false, shipType: null };
    },

    allShipsSunk() {
        return this.ships.every(s => s.isSunk());
    },

    // AI Attack Logic - Hunt/Target algorithm
    makeMove() {
        let row, col;

        // Target mode: attack adjacent cells to previous hits
        if (this.targetQueue.length > 0) {
            const target = this.targetQueue.shift();
            row = target.row;
            col = target.col;
        } else {
            // Hunt mode: use checkerboard pattern for efficiency
            do {
                row = Math.floor(Math.random() * gameConfig.boardSize);
                col = Math.floor(Math.random() * gameConfig.boardSize);
                // Checkerboard pattern: only hit cells where (row + col) is even
                if ((row + col) % 2 !== 0) {
                    col = (col + 1) % gameConfig.boardSize;
                }
            } while (this.attackHistory.has(`${row},${col}`));
        }

        this.attackHistory.add(`${row},${col}`);
        return { row, col };
    },

    processHitResult(row, col, hit) {
        if (hit) {
            this.lastHit = { row, col };
            // Add adjacent cells to target queue
            const adjacent = [
                { row: row - 1, col: col },
                { row: row + 1, col: col },
                { row: row, col: col - 1 },
                { row: row, col: col + 1 }
            ];

            for (const adj of adjacent) {
                if (adj.row >= 0 && adj.row < gameConfig.boardSize &&
                    adj.col >= 0 && adj.col < gameConfig.boardSize &&
                    !this.attackHistory.has(`${adj.row},${adj.col}`)) {
                    // Avoid duplicates in target queue
                    if (!this.targetQueue.some(t => t.row === adj.row && t.col === adj.col)) {
                        this.targetQueue.push(adj);
                    }
                }
            }
        }
    }
};

// Initialize SignalR connection
async function initializeConnection() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/gameHub")
        .withAutomaticReconnect()
        .build();

    // Set up event handlers
    setupSignalRHandlers();

    try {
        await connection.start();
        myConnectionId = connection.connectionId;

        // Check for active game to reconnect to
        await checkForActiveGame();
    } catch (err) {
        showNotification("Connection error. Please refresh the page.");
    }
}

async function checkForActiveGame() {
    const token = getPlayerToken();
    if (!token) return;

    try {
        const result = await connection.invoke("CheckForActiveGame", token);
        if (result.hasActiveGame) {
            // Show reconnection prompt
            showReconnectPrompt(result.roomCode, result.status);
        }
    } catch {
        // Silently fail - active game check is optional
    }
}

function showReconnectPrompt(roomCode, status) {
    const reconnectPrompt = document.createElement('div');
    reconnectPrompt.id = 'reconnect-prompt';
    reconnectPrompt.className = 'reconnect-prompt';
    reconnectPrompt.innerHTML = `
        <div class="reconnect-content">
            <h3>Active Game Found</h3>
            <p>You have an active game in room ${roomCode}.</p>
            <div class="reconnect-buttons">
                <button id="reconnect-yes-btn" class="btn btn-primary">Rejoin Game</button>
                <button id="reconnect-no-btn" class="btn btn-secondary">Start New Game</button>
            </div>
        </div>
    `;
    document.body.appendChild(reconnectPrompt);

    document.getElementById('reconnect-yes-btn').addEventListener('click', async () => {
        reconnectPrompt.remove();
        await reconnectToGame();
    });

    document.getElementById('reconnect-no-btn').addEventListener('click', () => {
        reconnectPrompt.remove();
        clearActiveGame();
    });
}

async function reconnectToGame() {
    const token = getPlayerToken();
    if (!token) {
        showNotification("No player token found");
        return;
    }

    try {
        showNotification("Reconnecting to game...");
        const result = await connection.invoke("ReconnectToGame", token);

        if (!result.success) {
            showNotification(result.message);
            clearActiveGame();
            return;
        }

        currentRoomCode = result.roomCode;
        gameConfig.size = result.size;
        gameConfig.boardSize = result.boardSize;
        gameConfig.shipTypes = Array.from(result.shipTypes);

        // Restore game state
        if (result.gameState) {
            restoreGameState(result.gameState, result.opponentConnected);
        } else {
            // Game hasn't started yet, go to setup
            showScreen('setup-screen');
            initializeSetupBoard();
            showNotification("Reconnected! Waiting for opponent...");
        }
    } catch {
        showNotification("Error reconnecting to game");
        clearActiveGame();
    }
}

function restoreGameState(gameState, opponentConnected) {
    // Restore the setup board from ship data
    initializeSetupBoardArray();

    if (gameState.yourShips && gameState.yourShips.length > 0) {
        gameState.yourShips.forEach(ship => {
            const coords = Array.from(ship.coordinates);
            const isHoriz = coords.length > 1 && coords[0].row === coords[1].row;

            coords.forEach(coord => {
                setupBoard[coord.row][coord.col] = ship.type;
            });
            placedShips.add(ship.type);
        });
    }

    if (gameState.state === 'Playing' || gameState.state === 'Finished') {
        // Restore battle screen
        showScreen('game-screen');
        initializeGameBoards();

        // Restore your attacks on opponent board
        if (gameState.yourAttacks) {
            gameState.yourAttacks.forEach(attack => {
                updateCell('opponent-board', attack.row, attack.col, attack.hit ? 'hit' : 'miss');
            });
        }

        // Restore opponent attacks on your board
        if (gameState.opponentAttacks) {
            gameState.opponentAttacks.forEach(attack => {
                updateCell('player-board', attack.row, attack.col, attack.hit ? 'hit' : 'miss');
            });
        }

        if (gameState.state === 'Finished') {
            showGameOver(gameState.winnerId === myConnectionId);
        } else {
            isMyTurn = gameState.isYourTurn;
            updateTurnIndicator();

            if (!opponentConnected) {
                showNotification("Reconnected! Waiting for opponent to reconnect...");
            } else {
                showNotification("Reconnected! Game restored.");
            }
        }

        addLogEntry("Reconnected to game");
    } else if (gameState.shipsPlaced) {
        // Ships placed but game hasn't started
        showNotification("Waiting for opponent to place ships...");
        showScreen('setup-screen');
        // Show ships as already placed but don't allow changes
        initializeSetupBoard();
        // Re-place the ships visually
        gameState.yourShips.forEach(ship => {
            const coords = Array.from(ship.coordinates);
            const isHoriz = coords.length > 1 && coords[0].row === coords[1].row;
            coords.forEach((coord, i) => {
                const cell = document.querySelector(`#setup-board .cell[data-row="${coord.row}"][data-col="${coord.col}"]`);
                if (cell) {
                    cell.classList.add('ship', 'ship-segment');
                    cell.classList.add(isHoriz ? 'ship-h' : 'ship-v');
                    cell.classList.add(getSegmentClass(i, coords.length, ship.type));
                }
            });
        });
        document.getElementById('ready-btn').disabled = true;
    } else {
        // Still in setup phase
        showScreen('setup-screen');
        initializeSetupBoard();
        showNotification("Reconnected! Place your ships.");
    }
}

// Set up SignalR event handlers
function setupSignalRHandlers() {
    connection.on("OpponentJoined", () => {
        showNotification("Opponent joined! Place your ships.");
        showScreen('setup-screen');
        initializeSetupBoard();
    });

    connection.on("ShipsPlaced", () => {
        showNotification("Ships placed! Waiting for opponent...");
    });

    connection.on("GameStarted", (firstPlayerConnectionId) => {
        showScreen('game-screen');
        initializeGameBoards();
        isMyTurn = (firstPlayerConnectionId === myConnectionId);
        updateTurnIndicator();
        addLogEntry("Game started!");
    });

    connection.on("AttackResult", (data) => {
        if (data.isAttacker) {
            // Update opponent board (targeting board)
            updateCell('opponent-board', data.row, data.col, data.hit ? 'hit' : 'miss');
            if (data.hit) {
                addLogEntry(`Hit! ${data.sunk ? 'You sunk their ' + data.shipType + '!' : ''}`, data.sunk ? 'sunk' : 'hit');
                if (!data.sunk) {
                    showNotification("Hit!");
                } else {
                    showNotification(`You sunk their ${data.shipType}!`);
                }
            } else {
                addLogEntry("Miss!", 'miss');
                showNotification("Miss!");
            }
        } else {
            // Update player board (defensive board)
            updateCell('player-board', data.row, data.col, data.hit ? 'hit' : 'miss');
            if (data.hit) {
                addLogEntry(`Opponent hit your ship! ${data.sunk ? 'Your ' + data.shipType + ' was sunk!' : ''}`, data.sunk ? 'sunk' : 'hit');
                if (data.sunk) {
                    showNotification(`Your ${data.shipType} was sunk!`);
                }
            } else {
                addLogEntry("Opponent missed!", 'miss');
            }
        }
    });

    connection.on("TurnChanged", (currentPlayerConnectionId) => {
        isMyTurn = (currentPlayerConnectionId === myConnectionId);
        updateTurnIndicator();
    });

    connection.on("GameOver", (winnerConnectionId) => {
        const didIWin = (winnerConnectionId === myConnectionId);
        showGameOver(didIWin);
    });

    connection.on("OpponentDisconnected", (data) => {
        if (data && data.canReconnect) {
            showNotification("Opponent disconnected. Waiting for them to reconnect...");
            // Don't reload - wait for potential reconnection
        } else {
            showNotification("Opponent left the game. Returning to home...");
            clearActiveGame();
            setTimeout(() => {
                location.reload();
            }, 3000);
        }
    });

    connection.on("OpponentReconnected", () => {
        showNotification("Opponent reconnected!");
    });

    connection.on("RandomShipsGenerated", (ships) => {
        // Clear current board
        clearSetupBoard();

        // Place the ships and track them
        ships.forEach(ship => {
            placeShipOnSetupBoard(ship.type, ship.startRow, ship.startCol, ship.isHorizontal);
            placedShips.add(ship.type);
        });

        updateShipsList();
        checkReadyState();
        showNotification("Ships randomly placed!");
    });
}

// Screen management
function showScreen(screenId) {
    document.querySelectorAll('.screen').forEach(screen => {
        screen.classList.remove('active');
    });
    document.getElementById(screenId).classList.add('active');
}

// Notification
function showNotification(message) {
    const notification = document.getElementById('notification');
    const text = document.getElementById('notification-text');
    text.textContent = message;
    notification.classList.remove('hidden');

    setTimeout(() => {
        notification.classList.add('hidden');
    }, 3000);
}

// Room management
async function createRoom() {
    try {
        const selectedSize = document.querySelector('input[name="game-size"]:checked').value;
        const playerToken = getOrCreatePlayerToken();
        const result = await connection.invoke("CreateRoom", selectedSize, playerToken);
        if (result.success) {
            currentRoomCode = result.roomCode;
            gameConfig.size = result.size;
            gameConfig.boardSize = result.boardSize;
            gameConfig.shipTypes = Array.from(result.shipTypes);
            // Store the token returned by server (may be different if server generated one)
            if (result.playerToken) {
                localStorage.setItem(PLAYER_TOKEN_KEY, result.playerToken);
            }
            saveActiveGame(result.roomCode);
            document.getElementById('room-code-display').textContent = result.roomCode;
            showScreen('waiting-screen');
        } else {
            showNotification(result.message);
        }
    } catch {
        showNotification("Error creating room");
    }
}

async function joinRoom(roomCode) {
    try {
        const playerToken = getOrCreatePlayerToken();
        const result = await connection.invoke("JoinRoom", roomCode, playerToken);
        if (result.success) {
            currentRoomCode = result.roomCode;
            gameConfig.size = result.size;
            gameConfig.boardSize = result.boardSize;
            gameConfig.shipTypes = Array.from(result.shipTypes);
            // Store the token returned by server
            if (result.playerToken) {
                localStorage.setItem(PLAYER_TOKEN_KEY, result.playerToken);
            }
            saveActiveGame(result.roomCode);
            showScreen('setup-screen');
            initializeSetupBoard();
        } else {
            showNotification(result.message);
        }
    } catch {
        showNotification("Error joining room");
    }
}

// Setup board
function initializeSetupBoard() {
    const board = document.getElementById('setup-board');
    board.innerHTML = '';
    initializeSetupBoardArray();
    placedShips = new Set();
    updateShipsList();
    updateShipSelector();

    // Set board size class
    board.className = `board board-${gameConfig.boardSize}`;

    for (let row = 0; row < gameConfig.boardSize; row++) {
        for (let col = 0; col < gameConfig.boardSize; col++) {
            const cell = document.createElement('div');
            cell.className = 'cell';
            cell.dataset.row = row;
            cell.dataset.col = col;

            cell.addEventListener('click', () => handleSetupCellClick(row, col));
            cell.addEventListener('mouseenter', () => showShipPreview(row, col));
            cell.addEventListener('mouseleave', () => clearPreviews());

            board.appendChild(cell);
        }
    }

    // Set initial ship type to first available
    if (gameConfig.shipTypes.length > 0) {
        currentShipType = gameConfig.shipTypes[0];
    }
}

function updateShipSelector() {
    const selector = document.getElementById('ship-selector');
    selector.innerHTML = '';

    gameConfig.shipTypes.forEach(shipType => {
        const option = document.createElement('option');
        option.value = shipType;
        option.textContent = `${shipType} (${shipSizes[shipType]})`;
        selector.appendChild(option);
    });

    if (gameConfig.shipTypes.length > 0) {
        selector.value = gameConfig.shipTypes[0];
        currentShipType = gameConfig.shipTypes[0];
    }
}

function handleSetupCellClick(row, col) {
    if (placedShips.has(currentShipType)) {
        showNotification("Ship already placed. Select a different ship.");
        return;
    }

    if (canPlaceShip(row, col, currentShipType, isHorizontal)) {
        placeShipOnSetupBoard(currentShipType, row, col, isHorizontal);
        placedShips.add(currentShipType);
        updateShipsList();
        clearPreviews();

        // Auto-select next ship from available ship types
        const currentIndex = gameConfig.shipTypes.indexOf(currentShipType);
        for (let i = currentIndex + 1; i < gameConfig.shipTypes.length; i++) {
            if (!placedShips.has(gameConfig.shipTypes[i])) {
                currentShipType = gameConfig.shipTypes[i];
                document.getElementById('ship-selector').value = currentShipType;
                break;
            }
        }

        checkReadyState();
    } else {
        showNotification("Invalid placement!");
    }
}

function canPlaceShip(row, col, shipType, horizontal) {
    const size = shipSizes[shipType];

    for (let i = 0; i < size; i++) {
        const r = horizontal ? row : row + i;
        const c = horizontal ? col + i : col;

        if (r >= gameConfig.boardSize || c >= gameConfig.boardSize) return false;
        if (setupBoard[r][c] !== null) return false;
    }

    return true;
}

function placeShipOnSetupBoard(shipType, row, col, horizontal) {
    const size = shipSizes[shipType];

    for (let i = 0; i < size; i++) {
        const r = horizontal ? row : row + i;
        const c = horizontal ? col + i : col;
        setupBoard[r][c] = shipType;

        const cell = document.querySelector(`#setup-board .cell[data-row="${r}"][data-col="${c}"]`);
        if (cell) {
            cell.classList.add('ship', 'ship-segment');
            cell.classList.add(horizontal ? 'ship-h' : 'ship-v');
            cell.classList.add(getSegmentClass(i, size, shipType));
        }
    }
}

function getSegmentClass(index, size, shipType = null) {
    if (size === 1) return 'ship-single';

    // Carrier: stern on both ends (squarish profile)
    if (shipType === 'Carrier') {
        if (index === 0) return 'ship-stern-front';
        if (index === size - 1) return 'ship-stern';
        return 'ship-mid';
    }

    // Battleship and Submarine: bow on both ends (pointed on both ends)
    if (shipType === 'Battleship' || shipType === 'Submarine') {
        if (index === 0) return 'ship-bow';
        if (index === size - 1) return 'ship-bow-end';
        return 'ship-mid';
    }

    // Default (Cruiser, Destroyer): bow at front, stern at back
    if (index === 0) return 'ship-bow';
    if (index === size - 1) return 'ship-stern';
    return 'ship-mid';
}

function showShipPreview(row, col) {
    if (placedShips.has(currentShipType)) return;

    clearPreviews();
    const valid = canPlaceShip(row, col, currentShipType, isHorizontal);
    const size = shipSizes[currentShipType];

    for (let i = 0; i < size; i++) {
        const r = isHorizontal ? row : row + i;
        const c = isHorizontal ? col + i : col;

        if (r < gameConfig.boardSize && c < gameConfig.boardSize) {
            const cell = document.querySelector(`#setup-board .cell[data-row="${r}"][data-col="${c}"]`);
            if (cell && !cell.classList.contains('ship')) {
                cell.classList.add(valid ? 'preview' : 'invalid');
                cell.classList.add('ship-segment');
                cell.classList.add(isHorizontal ? 'ship-h' : 'ship-v');
                cell.classList.add(getSegmentClass(i, size, currentShipType));
            }
        }
    }
}

function clearPreviews() {
    document.querySelectorAll('#setup-board .cell').forEach(cell => {
        if (!cell.classList.contains('ship')) {
            cell.classList.remove('preview', 'invalid', 'ship-segment', 'ship-h', 'ship-v', 'ship-bow', 'ship-bow-end', 'ship-mid', 'ship-stern', 'ship-stern-front', 'ship-single');
        } else {
            cell.classList.remove('preview', 'invalid');
        }
    });
}

function clearSetupBoard() {
    initializeSetupBoardArray();
    placedShips = new Set();
    updateShipsList();
    initializeSetupBoard();
    checkReadyState();
}

function updateShipsList() {
    // Ships list UI removed - function kept for compatibility
}

function checkReadyState() {
    const readyBtn = document.getElementById('ready-btn');
    readyBtn.disabled = placedShips.size !== gameConfig.shipTypes.length;
}

async function submitShips(useRandom = false) {
    const shipPlacements = [];

    if (!useRandom) {
        // Collect all placed ships
        gameConfig.shipTypes.forEach(shipType => {
            // Find the starting position of this ship
            let found = false;
            for (let row = 0; row < gameConfig.boardSize && !found; row++) {
                for (let col = 0; col < gameConfig.boardSize && !found; col++) {
                    if (setupBoard[row][col] === shipType) {
                        // Check if this is the start position
                        const isStart = (col === 0 || setupBoard[row][col - 1] !== shipType) &&
                                      (row === 0 || setupBoard[row - 1][col] !== shipType);

                        if (isStart) {
                            const isHoriz = (col < gameConfig.boardSize - 1 && setupBoard[row][col + 1] === shipType);
                            shipPlacements.push({
                                Type: shipType,
                                StartRow: row,
                                StartCol: col,
                                IsHorizontal: isHoriz
                            });
                            found = true;
                        }
                    }
                }
            }
        });

        // Validate we have all ships
        if (shipPlacements.length !== gameConfig.shipTypes.length) {
            showNotification("Error: Not all ships were collected properly");
            return;
        }
    }

    try {
        const result = await connection.invoke("PlaceShips", shipPlacements, useRandom);
        if (!result.success) {
            showNotification(result.message);
        }
    } catch {
        showNotification("Error placing ships");
    }
}

async function requestRandomShips() {
    try {
        await connection.invoke("RequestRandomShips");
    } catch {
        showNotification("Error generating random ships");
    }
}

// Game boards
function initializeGameBoards() {
    createBoard('player-board');
    createBoard('opponent-board');

    // Show player's ships on their board with proper segments
    showShipsOnBoard('player-board');

    // Add click handlers to opponent board
    document.querySelectorAll('#opponent-board .cell').forEach(cell => {
        cell.addEventListener('click', () => {
            const row = parseInt(cell.dataset.row);
            const col = parseInt(cell.dataset.col);
            handleAttack(row, col);
        });
    });
}

function showShipsOnBoard(boardId) {
    // Find each ship and apply segment classes
    const processedShips = new Set();

    for (let row = 0; row < gameConfig.boardSize; row++) {
        for (let col = 0; col < gameConfig.boardSize; col++) {
            const shipType = setupBoard[row][col];
            if (shipType && !processedShips.has(shipType)) {
                // Found start of a ship, determine orientation and place segments
                const shipCells = findShipCells(shipType, row, col);
                const isHorizontal = shipCells.length > 1 && shipCells[0].row === shipCells[1].row;

                shipCells.forEach((pos, index) => {
                    const cell = document.querySelector(`#${boardId} .cell[data-row="${pos.row}"][data-col="${pos.col}"]`);
                    if (cell) {
                        cell.classList.add('ship', 'ship-segment');
                        cell.classList.add(isHorizontal ? 'ship-h' : 'ship-v');
                        cell.classList.add(getSegmentClass(index, shipCells.length, shipType));
                    }
                });

                processedShips.add(shipType);
            }
        }
    }
}

function findShipCells(shipType, startRow, startCol) {
    const cells = [];

    // Check horizontal first
    let col = startCol;
    while (col < gameConfig.boardSize && setupBoard[startRow][col] === shipType) {
        cells.push({ row: startRow, col: col });
        col++;
    }

    if (cells.length > 1) return cells;

    // Check vertical
    cells.length = 0;
    let row = startRow;
    while (row < gameConfig.boardSize && setupBoard[row][startCol] === shipType) {
        cells.push({ row: row, col: startCol });
        row++;
    }

    return cells;
}

function createBoard(boardId) {
    const board = document.getElementById(boardId);
    board.innerHTML = '';

    // Set board size class
    board.className = board.className.replace(/board-\d+/g, '');
    board.classList.add(`board-${gameConfig.boardSize}`);

    for (let row = 0; row < gameConfig.boardSize; row++) {
        for (let col = 0; col < gameConfig.boardSize; col++) {
            const cell = document.createElement('div');
            cell.className = 'cell';
            cell.dataset.row = row;
            cell.dataset.col = col;
            board.appendChild(cell);
        }
    }
}

function updateCell(boardId, row, col, state) {
    const cell = document.querySelector(`#${boardId} .cell[data-row="${row}"][data-col="${col}"]`);
    if (cell) {
        cell.classList.add(state);
    }
}

async function handleAttack(row, col) {
    if (!isMyTurn) {
        showNotification("Not your turn!");
        return;
    }

    const cell = document.querySelector(`#opponent-board .cell[data-row="${row}"][data-col="${col}"]`);
    if (cell.classList.contains('hit') || cell.classList.contains('miss')) {
        showNotification("Already attacked this position!");
        return;
    }

    try {
        const result = await connection.invoke("Attack", row, col);
        if (!result.success) {
            showNotification(result.message);
        }
    } catch {
        showNotification("Error processing attack");
    }
}

function updateTurnIndicator() {
    const turnStatus = document.getElementById('turn-status');
    if (isMyTurn) {
        turnStatus.textContent = "Your Turn - Attack the enemy!";
        turnStatus.style.color = 'var(--success-color)';
    } else {
        turnStatus.textContent = "Opponent's Turn - Wait for their attack...";
        turnStatus.style.color = 'var(--warning-color)';
    }
}

function addLogEntry(message, type = '') {
    const logMessages = document.getElementById('log-messages');
    const entry = document.createElement('div');
    entry.className = `log-entry ${type}`;
    entry.textContent = `${new Date().toLocaleTimeString()}: ${message}`;
    logMessages.insertBefore(entry, logMessages.firstChild);

    // Keep only last 20 entries
    while (logMessages.children.length > 20) {
        logMessages.removeChild(logMessages.lastChild);
    }
}

function showGameOver(didWin) {
    showScreen('gameover-screen');
    const title = document.getElementById('result-title');
    const message = document.getElementById('result-message');

    // Clear active game since the game is over
    clearActiveGame();

    if (didWin) {
        title.textContent = "Victory!";
        title.style.color = 'var(--success-color)';
        message.textContent = "Congratulations! You sunk all enemy ships!";
    } else {
        title.textContent = "Defeat";
        title.style.color = 'var(--danger-color)';
        message.textContent = "All your ships were sunk. Better luck next time!";
    }
}

// Single Player Functions
function startSinglePlayerGame() {
    isSinglePlayer = true;
    BattleshipAI.reset();

    // Get selected game size
    const selectedSize = document.querySelector('input[name="game-size"]:checked').value;
    gameConfig.size = selectedSize;
    gameConfig.boardSize = selectedSize === 'Large' ? 10 : selectedSize === 'Medium' ? 9 : 8;
    gameConfig.shipTypes = selectedSize === 'Large'
        ? ['Carrier', 'Battleship', 'Cruiser', 'Submarine', 'Destroyer']
        : selectedSize === 'Medium'
            ? ['Battleship', 'Cruiser', 'Submarine', 'Destroyer']
            : ['Battleship', 'Cruiser', 'Destroyer'];

    // AI places ships
    BattleshipAI.placeShips();

    // Initialize player board for AI to track attacks
    playerBoard = Array(gameConfig.boardSize).fill(null).map(() => Array(gameConfig.boardSize).fill(null));

    showNotification("Single player game started! Place your ships.");
    showScreen('setup-screen');
    initializeSetupBoard();
}

function startSinglePlayerBattle() {
    showScreen('game-screen');
    initializeSinglePlayerBoards();
    isMyTurn = true;
    updateTurnIndicator();
    addLogEntry("Game started! You go first.");
}

function initializeSinglePlayerBoards() {
    createBoard('player-board');
    createBoard('opponent-board');

    // Copy player's ship placements to playerBoard for AI reference
    for (let row = 0; row < gameConfig.boardSize; row++) {
        for (let col = 0; col < gameConfig.boardSize; col++) {
            playerBoard[row][col] = setupBoard[row][col];
        }
    }

    // Show player's ships on their board
    showShipsOnBoard('player-board');

    // Add click handlers to opponent board for single player
    document.querySelectorAll('#opponent-board .cell').forEach(cell => {
        cell.addEventListener('click', () => {
            if (!isSinglePlayer) return;
            const row = parseInt(cell.dataset.row);
            const col = parseInt(cell.dataset.col);
            handleSinglePlayerAttack(row, col);
        });
    });
}

function handleSinglePlayerAttack(row, col) {
    if (!isMyTurn) {
        showNotification("Wait for your turn!");
        return;
    }

    const cell = document.querySelector(`#opponent-board .cell[data-row="${row}"][data-col="${col}"]`);
    if (cell.classList.contains('hit') || cell.classList.contains('miss')) {
        showNotification("Already attacked this position!");
        return;
    }

    // Process player attack on AI
    const result = BattleshipAI.processPlayerAttack(row, col);
    updateCell('opponent-board', row, col, result.hit ? 'hit' : 'miss');

    if (result.hit) {
        addLogEntry(`Hit! ${result.sunk ? 'You sunk their ' + result.shipType + '!' : ''}`, result.sunk ? 'sunk' : 'hit');
        showNotification(result.sunk ? `You sunk their ${result.shipType}!` : "Hit!");
    } else {
        addLogEntry("Miss!", 'miss');
        showNotification("Miss!");
    }

    // Check for player win
    if (BattleshipAI.allShipsSunk()) {
        showGameOver(true);
        return;
    }

    // AI turn
    isMyTurn = false;
    updateTurnIndicator();
    setTimeout(aiTurn, 1000); // Delay for dramatic effect
}

function aiTurn() {
    const move = BattleshipAI.makeMove();
    const shipType = playerBoard[move.row][move.col];
    const hit = shipType !== null;

    // Track hits on player ships
    let sunk = false;
    if (hit) {
        // Find and update the ship
        const shipInfo = findPlayerShipInfo(shipType);
        if (shipInfo) {
            shipInfo.hits.add(`${move.row},${move.col}`);
            sunk = shipInfo.hits.size === shipSizes[shipType];
        }
        BattleshipAI.processHitResult(move.row, move.col, true);
    }

    updateCell('player-board', move.row, move.col, hit ? 'hit' : 'miss');

    if (hit) {
        addLogEntry(`Computer hit your ship! ${sunk ? 'Your ' + shipType + ' was sunk!' : ''}`, sunk ? 'sunk' : 'hit');
        if (sunk) {
            showNotification(`Your ${shipType} was sunk!`);
        }
    } else {
        addLogEntry("Computer missed!", 'miss');
    }

    // Check for AI win
    if (allPlayerShipsSunk()) {
        showGameOver(false);
        return;
    }

    // Back to player turn
    isMyTurn = true;
    updateTurnIndicator();
}

// Track player ship hits for single player mode
let playerShipHits = {};

function findPlayerShipInfo(shipType) {
    if (!playerShipHits[shipType]) {
        playerShipHits[shipType] = { hits: new Set() };
    }
    return playerShipHits[shipType];
}

function allPlayerShipsSunk() {
    for (const shipType of gameConfig.shipTypes) {
        const shipInfo = playerShipHits[shipType];
        if (!shipInfo || shipInfo.hits.size < shipSizes[shipType]) {
            return false;
        }
    }
    return true;
}

function resetSinglePlayerState() {
    isSinglePlayer = false;
    playerBoard = [];
    playerShipHits = {};
    BattleshipAI.reset();
}

function placeRandomShipsClientSide() {
    // Clear current board
    clearSetupBoard();

    // Place ships randomly using same logic as AI
    for (const shipType of gameConfig.shipTypes) {
        let placed = false;
        let attempts = 0;

        while (!placed && attempts < 100) {
            const row = Math.floor(Math.random() * gameConfig.boardSize);
            const col = Math.floor(Math.random() * gameConfig.boardSize);
            const horizontal = Math.random() < 0.5;

            if (canPlaceShip(row, col, shipType, horizontal)) {
                placeShipOnSetupBoard(shipType, row, col, horizontal);
                placedShips.add(shipType);
                placed = true;
            }
            attempts++;
        }

        if (!placed) {
            // Start over if placement failed
            return placeRandomShipsClientSide();
        }
    }

    updateShipsList();
    checkReadyState();
    showNotification("Ships randomly placed!");
}

// Event Listeners
document.addEventListener('DOMContentLoaded', async () => {
    await initializeConnection();

    // Check for invite link room code and auto-join
    if (window.inviteRoomCode && window.inviteRoomCode.length === 6) {
        showNotification("Joining game...");
        joinRoom(window.inviteRoomCode.toUpperCase());
    }

    // Home screen
    document.getElementById('single-player-btn').addEventListener('click', startSinglePlayerGame);
    document.getElementById('create-room-btn').addEventListener('click', createRoom);

    document.getElementById('show-join-btn').addEventListener('click', () => {
        document.getElementById('join-room-form').classList.toggle('hidden');
    });

    document.getElementById('join-room-btn').addEventListener('click', () => {
        const roomCode = document.getElementById('room-code-input').value.trim().toUpperCase();
        if (roomCode.length === 6) {
            joinRoom(roomCode);
        } else {
            showNotification("Please enter a valid 6-character room code");
        }
    });

    document.getElementById('room-code-input').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            document.getElementById('join-room-btn').click();
        }
    });

    document.getElementById('copy-code-btn').addEventListener('click', async () => {
        // Invite URL is now at root since this is a standalone app
        const inviteUrl = `${window.location.origin}/?room=${currentRoomCode}`;
        try {
            await navigator.clipboard.writeText(inviteUrl);
            showNotification("Invite link copied!");
        } catch (err) {
            // Fallback for browsers without clipboard API or non-secure contexts
            const textArea = document.createElement('textarea');
            textArea.value = inviteUrl;
            textArea.style.position = 'fixed';
            textArea.style.left = '-9999px';
            document.body.appendChild(textArea);
            textArea.select();
            try {
                document.execCommand('copy');
                showNotification("Invite link copied!");
            } catch (fallbackErr) {
                showNotification("Copy failed - share this link: " + inviteUrl);
            }
            document.body.removeChild(textArea);
        }
    });

    // Setup screen
    document.getElementById('ship-selector').addEventListener('change', (e) => {
        currentShipType = e.target.value;
    });

    document.getElementById('orientation-toggle').addEventListener('change', (e) => {
        isHorizontal = e.target.checked;
    });

    document.getElementById('random-ships-btn').addEventListener('click', () => {
        if (isSinglePlayer) {
            // Single player: place ships client-side
            placeRandomShipsClientSide();
        } else {
            requestRandomShips();
        }
    });

    document.getElementById('clear-board-btn').addEventListener('click', clearSetupBoard);

    document.getElementById('ready-btn').addEventListener('click', () => {
        if (isSinglePlayer) {
            // Single player: start the battle immediately
            playerShipHits = {}; // Reset hit tracking
            startSinglePlayerBattle();
        } else {
            submitShips(false);
        }
    });

    // Game over screen
    document.getElementById('play-again-btn').addEventListener('click', () => {
        location.reload();
    });
});
