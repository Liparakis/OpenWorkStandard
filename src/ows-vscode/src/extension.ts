import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';

let statusBarItem: vscode.StatusBarItem;
let watchProcess: cp.ChildProcess | null = null;
let outputChannel: vscode.OutputChannel;
let extensionContext: vscode.ExtensionContext;

export function activate(context: vscode.ExtensionContext) {
    extensionContext = context;
    outputChannel = vscode.window.createOutputChannel("Open Work Standard");
    outputChannel.appendLine("OWS Extension Activated.");

    // Create status bar item
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.command = 'ows.showStatus';
    statusBarItem.text = '$(shield) OWS: Checking...';
    statusBarItem.tooltip = 'Click to show OWS Status';
    statusBarItem.show();
    context.subscriptions.push(statusBarItem);

    // Register commands
    registerCommand(context, 'ows.initialize', () => handleInitialize());
    registerCommand(context, 'ows.configure', () => handleConfigure(context));
    registerCommand(context, 'ows.startWatch', () => handleStartWatch(context));
    registerCommand(context, 'ows.stopWatch', () => handleStopWatch());
    registerCommand(context, 'ows.showStatus', () => handleShowStatus());
    registerCommand(context, 'ows.package', () => handlePackage());
    registerCommand(context, 'ows.upload', () => handleUpload());
    registerCommand(context, 'ows.checkStatus', () => handleCheckStatus());

    // Initial status update
    updateStatusBar();

    // Poll status bar status periodically (every 10 seconds)
    const interval = setInterval(() => updateStatusBar(), 10000);
    context.subscriptions.push({ dispose: () => clearInterval(interval) });
}

export function deactivate() {
    if (watchProcess) {
        watchProcess.kill();
        watchProcess = null;
    }
}

function registerCommand(context: vscode.ExtensionContext, id: string, callback: () => Thenable<any> | any) {
    const disposable = vscode.commands.registerCommand(id, async () => {
        try {
            await callback();
        } catch (err: any) {
            const redactedErr = redactApiKey(err.message || String(err));
            vscode.window.showErrorMessage(`OWS Command Error: ${redactedErr}`);
            outputChannel.appendLine(`[Error] [${id}] ${redactedErr}`);
        }
    });
    context.subscriptions.push(disposable);
}

// ── Command Handlers ────────────────────────────────────────────────────────

async function handleInitialize() {
    const workspaceRoot = await getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    outputChannel.appendLine("Initializing OWS project...");
    const result = await runCli(['init', '--json'], workspaceRoot);
    if (result.success) {
        vscode.window.showInformationMessage(result.message || "OWS initialized successfully.");
    } else {
        throw new Error(result.errors.join('\n'));
    }
    await updateStatusBar();
}

async function handleConfigure(context: vscode.ExtensionContext) {
    const workspaceRoot = await getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    // Read current settings or local config
    let currentConfig: any = {};
    const configPath = path.join(workspaceRoot, '.ows', 'config.json');
    if (fs.existsSync(configPath)) {
        try {
            currentConfig = JSON.parse(fs.readFileSync(configPath, 'utf8'));
        } catch { }
    }

    const verifierUrl = await vscode.window.showInputBox({
        prompt: 'Enter OWS Verifier URL',
        value: currentConfig.verifierUrl || vscode.workspace.getConfiguration('ows').get<string>('verifierUrl') || 'http://localhost:5078'
    });
    if (verifierUrl === undefined) return;

    const institutionId = await vscode.window.showInputBox({
        prompt: 'Enter Institution ID',
        value: currentConfig.institutionId || vscode.workspace.getConfiguration('ows').get<string>('institutionId') || ''
    });
    if (institutionId === undefined) return;

    const assessmentId = await vscode.window.showInputBox({
        prompt: 'Enter Assessment ID',
        value: currentConfig.assessmentId || vscode.workspace.getConfiguration('ows').get<string>('assessmentId') || ''
    });
    if (assessmentId === undefined) return;

    const studentUserId = await vscode.window.showInputBox({
        prompt: 'Enter Student User ID',
        value: currentConfig.studentUserId || vscode.workspace.getConfiguration('ows').get<string>('studentUserId') || ''
    });
    if (studentUserId === undefined) return;

    const courseOfferingId = await vscode.window.showInputBox({
        prompt: 'Enter Course Offering ID',
        value: currentConfig.courseOfferingId || vscode.workspace.getConfiguration('ows').get<string>('courseOfferingId') || ''
    });
    if (courseOfferingId === undefined) return;

    const apiKey = await vscode.window.showInputBox({
        prompt: 'Enter OWS Verifier API Key (stored securely, left blank to keep current)',
        password: true
    });

    if (apiKey) {
        // Validation/warning check
        if (apiKey.startsWith("op_") || apiKey.startsWith("admin_")) {
            vscode.window.showWarningMessage("Warning: You are providing an Operator or Admin API key for a student workflow.");
        } else if (!apiKey.startsWith("std_") && !apiKey.startsWith("student_")) {
            vscode.window.showWarningMessage("Warning: StudentClient key signature prefix not detected. Session operations may fail.");
        }
        await context.secrets.store("ows.apiKey", apiKey);
        outputChannel.appendLine("API Key saved to SecretStorage.");
    }

    // Save configuration parameters to .ows/config.json (avoiding secrets)
    const localOwsDir = path.join(workspaceRoot, '.ows');
    if (!fs.existsSync(localOwsDir)) {
        fs.mkdirSync(localOwsDir, { recursive: true });
    }

    const updatedConfig = {
        owsVersion: "0.1",
        projectRoot: workspaceRoot,
        initializedAtUtc: currentConfig.initializedAtUtc || new Date().toISOString(),
        verifierUrl,
        institutionId,
        assessmentId,
        studentUserId,
        courseOfferingId,
        packageUploadEnabled: currentConfig.packageUploadEnabled !== false
    };

    fs.writeFileSync(configPath, JSON.stringify(updatedConfig, null, 2), 'utf8');
    outputChannel.appendLine("Project configuration updated.");
    vscode.window.showInformationMessage("OWS project configuration saved.");
    await updateStatusBar();
}

async function handleStartWatch(context: vscode.ExtensionContext) {
    const workspaceRoot = await getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    const configPath = path.join(workspaceRoot, '.ows', 'config.json');
    if (!fs.existsSync(configPath)) {
        vscode.window.showErrorMessage("OWS is not initialized for this project. Please run 'Initialize OWS Project' first.");
        return;
    }

    outputChannel.appendLine("Starting OWS file watcher...");

    // Stop watch if already active locally
    if (watchProcess) {
        watchProcess.kill();
        watchProcess = null;
    }

    const cliPath = vscode.workspace.getConfiguration('ows').get<string>('cliPath') || 'ows';
    const cliCommandParts = cliPath.split(' ');
    const exec = cliCommandParts[0];
    const initialArgs = cliCommandParts.slice(1);

    const args = [...initialArgs, 'watch', 'start', '--json'];

    const apiKey = await getApiKey();
    if (!apiKey) {
        vscode.window.showWarningMessage("Warning: No OWS Verifier API Key is configured. Remote heartbeats/checkpoints will fail.");
    }

    let currentConfig: any = {};
    try {
        currentConfig = JSON.parse(fs.readFileSync(configPath, 'utf8'));
    } catch { }

    if (!currentConfig.verifierUrl) {
        vscode.window.showWarningMessage("Warning: Verifier URL is empty in OWS configuration.");
    }
    if (!currentConfig.institutionId || !currentConfig.assessmentId || !currentConfig.studentUserId) {
        vscode.window.showWarningMessage("Warning: Assessment context fields (Institution ID, Assessment ID, Student User ID) are missing in OWS configuration.");
    }

    const childEnv = {
        ...process.env,
        OWS_VERIFIER_API_KEY: apiKey,
        OWS_HOST: 'vscode'
    };

    try {
        watchProcess = cp.spawn(exec, args, { cwd: workspaceRoot, env: childEnv });
    } catch (err: any) {
        vscode.window.showErrorMessage(`Failed to start watcher process. OWS CLI executable not found at '${exec}'. Please check your 'ows.cliPath' setting.`);
        return;
    }
    
    watchProcess.on('error', (err: any) => {
        if (err.code === 'ENOENT') {
            vscode.window.showErrorMessage(`Failed to start watcher process. OWS CLI executable not found at '${exec}'. Please check your 'ows.cliPath' setting.`);
        } else {
            vscode.window.showErrorMessage(`Watcher process error: ${redactApiKey(err.message)}`);
        }
        watchProcess = null;
        updateStatusBar();
    });

    let started = false;
    watchProcess.stdout?.on('data', async (data) => {
        const text = data.toString();
        outputChannel.appendLine(`[Watcher Out] ${redactApiKey(text)}`);
        
        if (!started) {
            try {
                const response = JSON.parse(text);
                if (response.success) {
                    started = true;
                    vscode.window.showInformationMessage(response.message || "OWS file watcher started.");
                    await updateStatusBar();
                }
            } catch {
                // Ignore incomplete JSON chunks
            }
        }
    });

    watchProcess.stderr?.on('data', (data) => {
        outputChannel.appendLine(`[Watcher Err] ${redactApiKey(data.toString())}`);
    });

    watchProcess.on('exit', async (code) => {
        outputChannel.appendLine(`Watcher process exited with code ${code}`);
        watchProcess = null;
        await updateStatusBar();
    });
}

async function handleStopWatch() {
    const workspaceRoot = await getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    const configPath = path.join(workspaceRoot, '.ows', 'config.json');
    if (!fs.existsSync(configPath)) {
        vscode.window.showErrorMessage("OWS is not initialized for this project. Please run 'Initialize OWS Project' first.");
        return;
    }

    outputChannel.appendLine("Stopping OWS file watcher...");
    
    // Call CLI stop
    const result = await runCli(['watch', 'stop', '--json'], workspaceRoot);
    if (watchProcess) {
        watchProcess.kill();
        watchProcess = null;
    }

    if (result.success) {
        vscode.window.showInformationMessage(result.message || "OWS watch stopped.");
    } else {
        throw new Error(result.errors.join('\n'));
    }
    await updateStatusBar();
}

async function handleShowStatus() {
    const workspaceRoot = await getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    const configPath = path.join(workspaceRoot, '.ows', 'config.json');
    if (!fs.existsSync(configPath)) {
        vscode.window.showErrorMessage("OWS is not initialized for this project. Please run 'Initialize OWS Project' first.");
        return;
    }

    outputChannel.appendLine("Fetching OWS status...");
    const result = await runCli(['status', '--json'], workspaceRoot);
    if (result.success) {
        const details = [
            `Status: ${result.status}`,
            `Watcher Running: ${result.watcherRunning}`,
            `Session ID: ${result.sessionId ?? 'None'}`,
            `Verifier URL: ${result.verifierUrl ?? 'None'}`,
            `Institution ID: ${result.institutionId ?? 'None'}`,
            `Assessment ID: ${result.assessmentId ?? 'None'}`,
            `Student User ID: ${result.studentUserId ?? 'None'}`,
            `Course Offering ID: ${result.courseOfferingId ?? 'None'}`,
            `Last Checkpoint At: ${result.lastCheckpointAt ?? 'None'}`,
            `Last Heartbeat At: ${result.lastHeartbeatAt ?? 'None'}`
        ].join('\n');
        
        vscode.window.showInformationMessage(`OWS Project Status`, { detail: details, modal: true });
    } else {
        throw new Error(result.errors.join('\n'));
    }
}

async function handlePackage() {
    const workspaceRoot = await getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    const configPath = path.join(workspaceRoot, '.ows', 'config.json');
    if (!fs.existsSync(configPath)) {
        vscode.window.showErrorMessage("OWS is not initialized for this project. Please run 'Initialize OWS Project' first.");
        return;
    }

    outputChannel.appendLine("Packaging project...");
    vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: "Creating OWS submission package...",
        cancellable: false
    }, async () => {
        const result = await runCli(['package', '--json'], workspaceRoot);
        if (result.success) {
            vscode.window.showInformationMessage(result.message || "OWS package created successfully.");
        } else {
            throw new Error(result.errors.join('\n'));
        }
    });
}

async function handleUpload() {
    const workspaceRoot = await getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    const configPath = path.join(workspaceRoot, '.ows', 'config.json');
    if (!fs.existsSync(configPath)) {
        vscode.window.showErrorMessage("OWS is not initialized for this project. Please run 'Initialize OWS Project' first.");
        return;
    }

    outputChannel.appendLine("Uploading package...");
    vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: "Uploading OWS package...",
        cancellable: false
    }, async () => {
        const result = await runCli(['package', 'upload', '--json'], workspaceRoot);
        if (result.success) {
            vscode.window.showInformationMessage(`OWS Package Uploaded. Submission ID: ${result.packageId}`);
        } else {
            throw new Error(result.errors.join('\n'));
        }
    });
}

async function handleCheckStatus() {
    const workspaceRoot = await getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    const configPath = path.join(workspaceRoot, '.ows', 'config.json');
    if (!fs.existsSync(configPath)) {
        vscode.window.showErrorMessage("OWS is not initialized for this project. Please run 'Initialize OWS Project' first.");
        return;
    }

    outputChannel.appendLine("Checking verification status...");
    const result = await runCli(['package', 'status', '--json'], workspaceRoot);
    if (result.success) {
        vscode.window.showInformationMessage(`OWS Submission ID: ${result.packageId}\nVerification Status: ${result.status}\nTrust Status: ${result.trustStatus ?? 'Unknown'}`);
    } else {
        throw new Error(result.errors.join('\n'));
    }
}

// ── Helpers ─────────────────────────────────────────────────────────────────

async function getWorkspaceRoot(silent = false): Promise<string | undefined> {
    if (!vscode.workspace.isTrusted) {
        if (!silent) {
            vscode.window.showErrorMessage("OWS commands cannot run in an untrusted workspace. Please trust this workspace folder first.");
        }
        return undefined;
    }

    const folders = vscode.workspace.workspaceFolders;
    if (!folders || folders.length === 0) {
        if (!silent) {
            vscode.window.showErrorMessage("Open a workspace folder first to run OWS.");
        }
        return undefined;
    }
    if (folders.length === 1) {
        return folders[0].uri.fsPath;
    }

    // Check active text editor
    const activeEditor = vscode.window.activeTextEditor;
    if (activeEditor) {
        const workspaceFolder = vscode.workspace.getWorkspaceFolder(activeEditor.document.uri);
        if (workspaceFolder) {
            return workspaceFolder.uri.fsPath;
        }
    }

    if (silent) {
        return folders[0].uri.fsPath;
    }

    const items = folders.map(f => ({ label: f.name, description: f.uri.fsPath }));
    const picked = await vscode.window.showQuickPick(items, {
        placeHolder: "Select OWS workspace folder"
    });
    return picked ? picked.description : undefined;
}

async function runCli(args: string[], cwd: string): Promise<any> {
    const cliPath = vscode.workspace.getConfiguration('ows').get<string>('cliPath') || 'ows';
    const cliCommandParts = cliPath.split(' ');
    const exec = cliCommandParts[0];
    const initialArgs = cliCommandParts.slice(1);

    const apiKey = await getApiKey();

    return new Promise((resolve, reject) => {
        const childEnv = {
            ...process.env,
            OWS_VERIFIER_API_KEY: apiKey,
            OWS_HOST: 'vscode'
        };

        cp.execFile(exec, [...initialArgs, ...args], { cwd, env: childEnv }, (err, stdout, stderr) => {
            if (err) {
                if ((err as any).code === 'ENOENT') {
                    reject(new Error(`OWS CLI executable not found at: '${exec}'. Please check your 'ows.cliPath' setting.`));
                    return;
                }
                if (!stdout) {
                    reject(new Error(stderr || err.message));
                    return;
                }
            }
            try {
                const response = JSON.parse(stdout.trim());
                resolve(response);
            } catch (jsonErr) {
                const redactedStderr = redactApiKey(stderr || stdout);
                reject(new Error(`Failed to parse JSON response from CLI. Output was: ${redactedStderr}`));
            }
        });
    });
}

let cachedApiKey = "";
async function getApiKey(): Promise<string> {
    try {
        if (extensionContext) {
            const key = await extensionContext.secrets.get("ows.apiKey");
            if (key) {
                cachedApiKey = key;
                return key;
            }
        }
    } catch { }

    const key = process.env.OWS_VERIFIER_API_KEY || "";
    cachedApiKey = key;
    return key;
}

async function updateStatusBar() {
    const workspaceRoot = await getWorkspaceRoot(true);
    if (!workspaceRoot) {
        statusBarItem.text = '$(shield) OWS: Untrusted';
        statusBarItem.backgroundColor = undefined;
        return;
    }

    try {
        const result = await runCli(['status', '--json'], workspaceRoot);
        if (result.success) {
            let statusText = result.status || 'Ready';
            statusBarItem.backgroundColor = undefined;

            if (statusText === 'WatchingLocalOnly') {
                statusBarItem.text = `$(eye) OWS: Watching (Local)`;
            } else if (statusText === 'SessionActive') {
                if (result.watcherRunning) {
                    statusBarItem.text = `$(pulse) OWS: Watching & Session active`;
                    statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.warningBackground');
                } else {
                    statusBarItem.text = `$(check) OWS: Session active`;
                }
            } else if (statusText === 'VerifierOffline') {
                statusBarItem.text = `$(warning) OWS: Verifier Offline`;
                statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
            } else if (statusText === 'HeartbeatFailing') {
                statusBarItem.text = `$(alert) OWS: Heartbeat Failing`;
                statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
            } else if (statusText === 'Degraded') {
                statusBarItem.text = `$(warning) OWS: Degraded`;
                statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.warningBackground');
            } else if (statusText === 'Error') {
                statusBarItem.text = `$(alert) OWS: Error`;
                statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
            } else if (statusText === 'Not Initialized') {
                statusBarItem.text = '$(alert) OWS: Not initialized';
            } else {
                statusBarItem.text = `$(shield) OWS: ${statusText}`;
            }

            statusBarItem.tooltip = `OWS Status: ${statusText}\nSession: ${result.sessionId ?? 'None'}\nWatcher: ${result.watcherRunning ? 'Running' : 'Stopped'}`;
            if (result.errors && result.errors.length > 0) {
                statusBarItem.tooltip += `\nErrors:\n${result.errors.join('\n')}`;
            }
        } else {
            statusBarItem.text = '$(alert) OWS: Not initialized';
            statusBarItem.backgroundColor = undefined;
            statusBarItem.tooltip = result.errors.join('\n');
        }
    } catch (err: any) {
        statusBarItem.text = '$(warning) OWS: Offline';
        statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
        statusBarItem.tooltip = redactApiKey(err.message || String(err));
    }
}

function redactApiKey(input: string): string {
    let result = input;
    if (cachedApiKey && cachedApiKey.length >= 6) {
        const escapedKey = cachedApiKey.replace(/[-\/\\^$*+?.()|[\]{}]/g, '\\$&');
        result = result.replace(new RegExp(escapedKey, 'g'), "[REDACTED_API_KEY]");
    }
    const envKey = process.env.OWS_VERIFIER_API_KEY || "";
    if (envKey && envKey.length >= 6 && envKey !== cachedApiKey) {
        const escapedEnvKey = envKey.replace(/[-\/\\^$*+?.()|[\]{}]/g, '\\$&');
        result = result.replace(new RegExp(escapedEnvKey, 'g'), "[REDACTED_API_KEY]");
    }
    return result;
}
