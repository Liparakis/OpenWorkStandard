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
    const workspaceRoot = getWorkspaceRoot();
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
    const workspaceRoot = getWorkspaceRoot();
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
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) { return; }

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

    const apiKey = await context.secrets.get("ows.apiKey") || "";
    if (!apiKey) {
        vscode.window.showWarningMessage("Warning: No OWS Verifier API Key is configured. Remote heartbeats/checkpoints will fail.");
    }

    const childEnv = {
        ...process.env,
        OWS_VERIFIER_API_KEY: apiKey
    };

    watchProcess = cp.spawn(exec, args, { cwd: workspaceRoot, env: childEnv });
    
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
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) { return; }

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
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) { return; }

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
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) { return; }

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
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) { return; }

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
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) { return; }

    outputChannel.appendLine("Checking verification status...");
    const result = await runCli(['package', 'status', '--json'], workspaceRoot);
    if (result.success) {
        vscode.window.showInformationMessage(`OWS Submission ID: ${result.packageId}\nVerification Status: ${result.status}\nTrust Status: ${result.trustStatus ?? 'Unknown'}`);
    } else {
        throw new Error(result.errors.join('\n'));
    }
}

// ── Helpers ─────────────────────────────────────────────────────────────────

function getWorkspaceRoot(): string | undefined {
    const folders = vscode.workspace.workspaceFolders;
    if (!folders || folders.length === 0) {
        vscode.window.showErrorMessage("Open a workspace folder first to run OWS.");
        return undefined;
    }
    return folders[0].uri.fsPath;
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
            OWS_VERIFIER_API_KEY: apiKey
        };

        cp.execFile(exec, [...initialArgs, ...args], { cwd, env: childEnv }, (err, stdout, stderr) => {
            if (err && !stdout) {
                reject(new Error(stderr || err.message));
                return;
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

async function getApiKey(): Promise<string> {
    try {
        if (extensionContext) {
            const key = await extensionContext.secrets.get("ows.apiKey");
            if (key) return key;
        }
    } catch { }

    return process.env.OWS_VERIFIER_API_KEY || "";
}

async function updateStatusBar() {
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) {
        statusBarItem.text = '$(shield) OWS: No folder';
        statusBarItem.backgroundColor = undefined;
        return;
    }

    try {
        const result = await runCli(['status', '--json'], workspaceRoot);
        if (result.success) {
            let icon = '$(shield)';
            let statusText = result.status || 'Ready';

            if (result.watcherRunning && result.sessionId) {
                statusBarItem.text = `$(pulse) OWS: Watching & Session active`;
                statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.warningBackground'); // warm active
            } else if (result.watcherRunning) {
                statusBarItem.text = `$(eye) OWS: Watching`;
                statusBarItem.backgroundColor = undefined;
            } else if (result.sessionId) {
                statusBarItem.text = `$(check) OWS: Session active`;
                statusBarItem.backgroundColor = undefined;
            } else {
                statusBarItem.text = `$(shield) OWS: Ready`;
                statusBarItem.backgroundColor = undefined;
            }
            statusBarItem.tooltip = `OWS Status: ${statusText}\nSession: ${result.sessionId ?? 'None'}\nWatcher: ${result.watcherRunning ? 'Running' : 'Stopped'}`;
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
    const apiKey = process.env.OWS_VERIFIER_API_KEY || "";
    if (!apiKey || apiKey.length < 6) return input;
    return input.replace(new RegExp(apiKey, 'g'), "[REDACTED_API_KEY]");
}
