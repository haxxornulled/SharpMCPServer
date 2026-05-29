import { existsSync, readFileSync, writeFileSync } from "node:fs";
import * as path from "node:path";
import * as vscode from "vscode";

const MISSING_WORKSPACE_MESSAGE = "Open the MCPServer repository folder first.";
const WORKSPACE_STATUS_COMMAND = "mcpserver.showWorkspaceActions";
const CONFIGURE_INFERENCE_ROUTING_COMMAND = "mcpserver.configureInferenceRouting";
const CONFIGURE_INFERENCE_PROVIDER_MODEL_COMMAND = "mcpserver.configureInferenceProviderModel";
const CONFIGURE_INFERENCE_PROVIDER_PRIORITY_COMMAND = "mcpserver.configureInferenceProviderPriority";
const CONFIGURE_INFERENCE_PROVIDER_PERFORMANCE_COMMAND = "mcpserver.configureInferenceProviderPerformance";
const CONFIGURE_INFERENCE_PROVIDER_API_KEY_COMMAND = "mcpserver.configureInferenceProviderApiKey";
const WORKSPACE_VIEW_ID = "mcpserver.workspace";
const UI_AUTO_OPENED_KEY = "mcpserver.ui.autoOpened";
const DEBUG_OUTPUT_CONFIGURATION = "Debug";
const TARGET_FRAMEWORK = "net10.0";
const CHAT_LAUNCHER_PROJECT = "MCPServer.ChatLauncher";
const HOST_PROJECT = "MCPServer.Host";
const HOST_APPSETTINGS_FILE = "appsettings.json";

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  const workspaceStatusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  workspaceStatusBarItem.command = WORKSPACE_STATUS_COMMAND;
  context.subscriptions.push(workspaceStatusBarItem);

  const provider = new McpServerWorkspaceViewProvider();
  const chatTerminalManager = new ChatTerminalManager();
  context.subscriptions.push(
    vscode.window.registerTreeDataProvider(WORKSPACE_VIEW_ID, provider),
    vscode.window.onDidCloseTerminal((closedTerminal) => {
      chatTerminalManager.handleTerminalClosed(closedTerminal);
    }),
    vscode.commands.registerCommand("mcpserver.openChatConsole", async () => {
      await chatTerminalManager.openOrRevealAsync();
    }),
    vscode.commands.registerCommand("mcpserver.showWorkspaceRoot", async () => {
      await showWorkspaceRoot();
    }),
    vscode.commands.registerCommand("mcpserver.copyWorkspaceRoot", async () => {
      await copyWorkspaceRoot();
    }),
    vscode.commands.registerCommand("mcpserver.openWorkspaceRootInExplorer", async () => {
      await openWorkspaceRootInExplorer();
    }),
    vscode.commands.registerCommand(CONFIGURE_INFERENCE_PROVIDER_MODEL_COMMAND, async (providerId?: string) => {
      await configureInferenceProviderModel(providerId, provider, workspaceStatusBarItem);
    }),
    vscode.commands.registerCommand(CONFIGURE_INFERENCE_PROVIDER_PRIORITY_COMMAND, async (providerId?: string) => {
      await configureInferenceProviderPriority(providerId, provider, workspaceStatusBarItem);
    }),
    vscode.commands.registerCommand(CONFIGURE_INFERENCE_PROVIDER_PERFORMANCE_COMMAND, async (providerId?: string) => {
      await configureInferenceProviderPerformance(providerId, provider, workspaceStatusBarItem);
    }),
    vscode.commands.registerCommand(CONFIGURE_INFERENCE_PROVIDER_API_KEY_COMMAND, async (providerId?: string) => {
      await configureInferenceProviderApiKey(providerId, provider, workspaceStatusBarItem);
    }),
    vscode.commands.registerCommand("mcpserver.refreshMcpServerState", async () => {
      await refreshMcpServerState(provider, workspaceStatusBarItem);
    }),
    vscode.commands.registerCommand(WORKSPACE_STATUS_COMMAND, async () => {
      await showWorkspaceActions(provider, workspaceStatusBarItem, chatTerminalManager);
    }),
    vscode.commands.registerCommand("mcpserver.refreshWorkspaceView", () => provider.refresh()),
    vscode.window.onDidChangeActiveTextEditor(() => {
      refreshWorkspacePresentation(provider, workspaceStatusBarItem);
    }),
    vscode.workspace.onDidChangeWorkspaceFolders(() => {
      refreshWorkspacePresentation(provider, workspaceStatusBarItem);
    })
  );

  refreshWorkspacePresentation(provider, workspaceStatusBarItem);
  await showFirstRunUi(context, chatTerminalManager);
}

export function deactivate(): void {
  // No cleanup needed yet.
}

async function showWorkspaceActions(
  provider: McpServerWorkspaceViewProvider,
  statusBarItem: vscode.StatusBarItem,
  chatTerminalManager: ChatTerminalManager
): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  const items: WorkspaceActionItem[] = [
    {
      label: "Open Chat Console",
      description: "Launch the repo-owned chat REPL through the repo-local wrapper",
      action: async () => chatTerminalManager.openOrRevealAsync(),
    },
    {
      label: "Configure Inference Provider Model",
      description: "Choose a live model for any configured inference provider",
      action: async () => configureInferenceProviderModel(undefined, provider, statusBarItem),
    },
    {
      label: "Configure Inference Routing",
      description: "Adjust tandem validation, validator model, and routing priority",
      action: async () => configureInferenceRouting(provider, statusBarItem),
    },
    {
      label: "Configure Inference Provider Priority",
      description: "Choose which providers run first for second-opinion and tandem routing",
      action: async () => configureInferenceProviderPriority(undefined, provider, statusBarItem),
    },
    {
      label: "Configure Inference Provider Performance",
      description: "Tune provider defaults like tokens, temperature, and context length",
      action: async () => configureInferenceProviderPerformance(undefined, provider, statusBarItem),
    },
    {
      label: "Configure Inference Provider API Key",
      description: "Store an API key or bearer token for a cloud inference provider",
      action: async () => configureInferenceProviderApiKey(undefined, provider, statusBarItem),
    },
    {
      label: "Show Workspace Root",
      description: workspaceRoot ?? "No workspace folder is open",
      action: async () => showWorkspaceRoot(),
    },
    {
      label: "Copy Workspace Root",
      description: workspaceRoot ?? "No workspace folder is open",
      action: async () => copyWorkspaceRoot(),
    },
    {
      label: "Open Workspace Root in Explorer",
      description: workspaceRoot ?? "No workspace folder is open",
      action: async () => openWorkspaceRootInExplorer(),
    },
    {
      label: "Refresh MCPServer State",
      description: "Re-read workspace state and refresh the MCPServer sidebar",
      action: async () => refreshMcpServerState(provider, statusBarItem),
    },
    {
      label: "Refresh Workspace View",
      description: "Re-read the current workspace state",
      action: async () => vscode.commands.executeCommand("mcpserver.refreshWorkspaceView"),
    }
  ];

  const selectedItem = await vscode.window.showQuickPick(items, {
    title: "MCPServer workspace actions",
    placeHolder: workspaceRoot ?? MISSING_WORKSPACE_MESSAGE,
    matchOnDescription: true
  });

  if (selectedItem === undefined) {
    return;
  }

  await selectedItem.action();
}

async function showWorkspaceRoot(): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return;
  }

  await vscode.window.showInformationMessage(`MCPServer workspace root: ${workspaceRoot}`);
}

async function copyWorkspaceRoot(): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return;
  }

  await vscode.env.clipboard.writeText(workspaceRoot);
  await vscode.window.showInformationMessage("Copied the MCPServer workspace root to the clipboard.");
}

async function openWorkspaceRootInExplorer(): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return;
  }

  await vscode.env.openExternal(vscode.Uri.file(workspaceRoot));
}

async function configureInferenceProviderModel(
  providerId: string | undefined,
  provider: McpServerWorkspaceViewProvider,
  statusBarItem: vscode.StatusBarItem
): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return;
  }

  const providers = readInferenceProviders(workspaceRoot);
  if (providers.length === 0) {
    await vscode.window.showWarningMessage("No inference providers were found in MCPServer.Host/appsettings.json.");
    return;
  }

  const selectedProvider = providerId === undefined
    ? await selectInferenceProvider(providers)
    : providers.find((candidate) => candidate.providerId === providerId);

  if (selectedProvider === undefined) {
    return;
  }

  const selectedModel = await selectInferenceProviderModel(selectedProvider);
  if (selectedModel === undefined) {
    return;
  }

  await updateInferenceProviderModel(workspaceRoot, selectedProvider.providerId, selectedModel);
  refreshWorkspacePresentation(provider, statusBarItem);
  await vscode.window.showInformationMessage(
    `Set ${selectedProvider.providerId} model to ${selectedModel}. Restart the chat console or host to apply the change.`
  );
}

async function configureInferenceProviderApiKey(
  providerId: string | undefined,
  provider: McpServerWorkspaceViewProvider,
  statusBarItem: vscode.StatusBarItem
): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return;
  }

  const providers = readInferenceProviders(workspaceRoot);
  if (providers.length === 0) {
    await vscode.window.showWarningMessage("No inference providers were found in MCPServer.Host/appsettings.json.");
    return;
  }

  const selectedProvider = providerId === undefined
    ? await selectInferenceProvider(providers)
    : providers.find((candidate) => candidate.providerId === providerId);

  if (selectedProvider === undefined) {
    return;
  }

  const apiKey = await vscode.window.showInputBox({
    title: `API key for ${selectedProvider.providerId}`,
    prompt: "Enter the API key or bearer token. Leave blank to clear it.",
    placeHolder: selectedProvider.apiKey ? "An API key is already configured." : "Enter the API key or bearer token.",
    password: true,
    ignoreFocusOut: true
  });

  if (apiKey === undefined) {
    return;
  }

  await updateInferenceProviderApiKey(
    workspaceRoot,
    selectedProvider.providerId,
    apiKey.trim().length === 0 ? undefined : apiKey.trim()
  );
  refreshWorkspacePresentation(provider, statusBarItem);
  await vscode.window.showInformationMessage(
    `Updated the API key for ${selectedProvider.providerId}. Restart the chat console or host to apply the change.`
  );
}

async function configureInferenceRouting(
  provider: McpServerWorkspaceViewProvider,
  statusBarItem: vscode.StatusBarItem
): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return;
  }

  const routingSettings = readInferenceRoutingSettings(workspaceRoot);
  const selectedAction = await vscode.window.showQuickPick<WorkspaceActionItem>(
    [
      {
        label: "Set default strategy",
        description: routingSettings.defaultStrategy || "PrimaryThenFallback",
        action: async () => configureDefaultRoutingStrategy(workspaceRoot)
      },
      {
        label: "Toggle tandem validation",
        description: routingSettings.tandemValidationEnabled ? "enabled" : "disabled",
        action: async () => configureTandemValidationEnabled(workspaceRoot)
      },
      {
        label: "Set tandem candidate count",
        description: `${routingSettings.tandemCandidateCount}`,
        action: async () => configureTandemCandidateCount(workspaceRoot)
      },
      {
        label: "Set tandem validator provider",
        description: routingSettings.tandemValidationProviderId || "not configured",
        action: async () => configureTandemValidationProvider(workspaceRoot)
      },
      {
        label: "Set tandem validator model",
        description: routingSettings.tandemValidationModel || "not configured",
        action: async () => configureTandemValidationModel(workspaceRoot)
      },
      {
        label: "Set provider routing priority",
        description: "Choose which provider runs first for tandem and second opinion",
        action: async () => configureInferenceProviderPriority(undefined, provider, statusBarItem)
      }
    ],
    {
      title: "Configure inference routing",
      placeHolder: "Choose the routing setting to edit",
      matchOnDescription: true
    }
  );

  if (selectedAction === undefined) {
    return;
  }

  await selectedAction.action();
  refreshWorkspacePresentation(provider, statusBarItem);
}

async function configureInferenceProviderPriority(
  providerId: string | undefined,
  provider: McpServerWorkspaceViewProvider,
  statusBarItem: vscode.StatusBarItem
): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return;
  }

  const providers = readInferenceProviders(workspaceRoot);
  if (providers.length === 0) {
    await vscode.window.showWarningMessage("No inference providers were found in MCPServer.Host/appsettings.json.");
    return;
  }

  const selectedProvider = providerId === undefined
    ? await selectInferenceProvider(providers)
    : providers.find((candidate) => candidate.providerId === providerId);

  if (selectedProvider === undefined) {
    return;
  }

  const routingPriority = await promptForIntegerValue(
    `Routing priority for ${selectedProvider.providerId}`,
    "Lower numbers run earlier. Leave blank to clear the priority.",
    selectedProvider.routingPriority
  );

  if (routingPriority.cancelled) {
    return;
  }

  if (routingPriority.value === undefined) {
    await updateInferenceProviderSetting(workspaceRoot, selectedProvider.providerId, "RoutingPriority", undefined);
  } else {
    await updateInferenceProviderSetting(workspaceRoot, selectedProvider.providerId, "RoutingPriority", routingPriority.value);
  }

  refreshWorkspacePresentation(provider, statusBarItem);
  await vscode.window.showInformationMessage(
    `Updated the routing priority for ${selectedProvider.providerId}. Restart the chat console or host to apply the change.`
  );
}

async function configureInferenceProviderPerformance(
  providerId: string | undefined,
  provider: McpServerWorkspaceViewProvider,
  statusBarItem: vscode.StatusBarItem
): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return;
  }

  const providers = readInferenceProviders(workspaceRoot);
  if (providers.length === 0) {
    await vscode.window.showWarningMessage("No inference providers were found in MCPServer.Host/appsettings.json.");
    return;
  }

  const selectedProvider = providerId === undefined
    ? await selectInferenceProvider(providers)
    : providers.find((candidate) => candidate.providerId === providerId);

  if (selectedProvider === undefined) {
    return;
  }

  const selectedField = await vscode.window.showQuickPick<ProviderPerformanceQuickPickItem>(
    getPerformanceSettingItems(selectedProvider).map((field) => ({
      label: field.label,
      description: field.description,
      field
    })),
    {
      title: `Performance tuning for ${selectedProvider.providerId}`,
      placeHolder: "Choose the provider setting to edit",
      matchOnDescription: true
    }
  );

  if (selectedField === undefined) {
    return;
  }

  const updatedValue = await selectedField.field.prompt(selectedProvider);
  if (updatedValue.cancelled) {
    return;
  }

  await updateInferenceProviderSetting(
    workspaceRoot,
    selectedProvider.providerId,
    selectedField.field.propertyName,
    updatedValue.value
  );

  refreshWorkspacePresentation(provider, statusBarItem);
  await vscode.window.showInformationMessage(
    `Updated the ${selectedField.field.label.toLowerCase()} for ${selectedProvider.providerId}. Restart the chat console or host to apply the change.`
  );
}

async function selectInferenceProvider(providers: InferenceProviderConfig[]): Promise<InferenceProviderConfig | undefined> {
  const selection = await vscode.window.showQuickPick(
    providers.map((provider) => ({
      label: provider.providerId,
      description: provider.model || "provider default",
      detail: `${provider.baseAddress || "No base address configured"}${provider.routingPriority !== 0 ? ` | priority ${provider.routingPriority}` : ""}`,
      provider
    })),
    {
      title: "Choose an inference provider",
      placeHolder: "Select a configured inference provider to update its settings",
      matchOnDescription: true,
      matchOnDetail: true
    }
  );

  return selection?.provider;
}

async function selectInferenceProviderModel(provider: InferenceProviderConfig): Promise<string | undefined> {
  const liveModels = await loadLiveInferenceModels(provider);
  const modelItems = liveModels
    .map((model) => ({
      label: model.label,
      description: model.identifier === provider.model ? "current" : undefined,
      detail: model.detail,
      model
    }))
    .sort((left, right) => {
      if (left.model.identifier === provider.model) {
        return -1;
      }

      if (right.model.identifier === provider.model) {
        return 1;
      }

      return left.label.localeCompare(right.label);
    });

  const selectedModel = await vscode.window.showQuickPick(
    [
      ...modelItems,
      {
        label: "Custom model...",
        description: "Enter a model name manually",
        model: undefined
      }
    ],
    {
      title: `Choose a model for ${provider.providerId}`,
      placeHolder: provider.baseAddress || "Select a live model or enter one manually",
      matchOnDescription: true
    }
  );

  if (selectedModel === undefined) {
    return undefined;
  }

  if (selectedModel.model !== undefined) {
    return selectedModel.model.identifier;
  }

  const modelInput = await vscode.window.showInputBox({
    title: `Model for ${provider.providerId}`,
    prompt: "Enter the model name to write into the host configuration",
    value: provider.model
  });

  if (modelInput === undefined) {
    return undefined;
  }

  const trimmed = modelInput.trim();
  return trimmed.length === 0 || trimmed.toLowerCase() === "clear" ? undefined : trimmed;
}

async function loadLiveInferenceModels(provider: InferenceProviderConfig): Promise<LiveInferenceModel[]> {
  if (stringIsBlank(provider.baseAddress)) {
    return [];
  }

  const abortController = new AbortController();
  const timeoutHandle = setTimeout(() => abortController.abort(), 5_000);

  try {
    const modelsUrl = buildLiveInferenceModelsUrl(provider);
    if (modelsUrl === undefined) {
      return [];
    }

    const headers: Record<string, string> = {
      Accept: "application/json"
    };

    if (!stringIsBlank(provider.apiKey)) {
      headers.Authorization = `Bearer ${provider.apiKey}`;
    }

    const response = await fetch(modelsUrl, {
      method: "GET",
      headers,
      signal: abortController.signal
    });

    if (!response.ok) {
      return [];
    }

    const payload = (await response.json()) as unknown;
    return extractLiveInferenceModels(payload, provider.providerId);
  } catch {
    return [];
  } finally {
    clearTimeout(timeoutHandle);
  }
}

function extractLiveInferenceModels(payload: unknown, providerId: string): LiveInferenceModel[] {
  if (!isRecord(payload)) {
    return [];
  }

  const candidates = [payload.data, payload.models];
  for (const candidate of candidates) {
    if (!Array.isArray(candidate)) {
      continue;
    }

    const models = candidate
      .map((item) => extractLiveInferenceModel(item, providerId))
      .filter((model): model is LiveInferenceModel => model !== undefined);

    if (models.length > 0) {
      return models;
    }
  }

  return [];
}

function extractLiveInferenceModel(item: unknown, providerId: string): LiveInferenceModel | undefined {
  if (!isRecord(item)) {
    return undefined;
  }

  const normalizedProviderId = providerId.trim().toLowerCase();
  if (normalizedProviderId === "ollama") {
    const identifier = readString(item["name"]) || readString(item["model"]) || readString(item["id"]) || readString(item["key"]);
    if (stringIsBlank(identifier)) {
      return undefined;
    }

    const detailsValue = item["details"];
    const details = isRecord(detailsValue) ? detailsValue : undefined;
    const detailParts = [
      readString(details === undefined ? undefined : details["parameter_size"]),
      readString(details === undefined ? undefined : details["quantization_level"]),
      formatBytes(readOptionalNumber(item["size"]))
    ].filter((value): value is string => !stringIsBlank(value));

    return {
      identifier: identifier.trim(),
      label: identifier.trim(),
      detail: detailParts.length > 0 ? detailParts.join(" | ") : undefined
    };
  }

  if (normalizedProviderId === "lmstudio") {
    const identifier = readString(item["key"]) || readString(item["id"]) || readString(item["name"]);
    if (stringIsBlank(identifier)) {
      return undefined;
    }

    const displayName = readString(item["display_name"]) || identifier.trim();
    const quantizationValue = item["quantization"];
    const quantization = isRecord(quantizationValue)
      ? [
          readString(quantizationValue["name"]),
          readOptionalNumber(quantizationValue["bits_per_weight"]) !== undefined
            ? `${readOptionalNumber(quantizationValue["bits_per_weight"])} bits`
            : undefined
        ]
      : [];
    const detailParts = [
      readString(item["params_string"]),
      readString(item["architecture"]),
      ...quantization,
      formatBytes(readOptionalNumber(item["size_bytes"]))
    ].filter((value): value is string => !stringIsBlank(value));

    return {
      identifier: identifier.trim(),
      label: displayName.trim(),
      detail: detailParts.length > 0 ? detailParts.join(" | ") : undefined
    };
  }

  const identifier = readString(item.id) || readString(item.key) || readString(item.model) || readString(item.name);
  if (stringIsBlank(identifier)) {
    return undefined;
  }

  return {
    identifier: identifier.trim(),
    label: identifier.trim(),
    detail: undefined
  };
}

async function updateInferenceProviderModel(
  workspaceRoot: string,
  providerId: string,
  model: string
): Promise<void> {
  await updateInferenceProviderSetting(workspaceRoot, providerId, "Model", model);
}

async function updateInferenceProviderApiKey(
  workspaceRoot: string,
  providerId: string,
  apiKey: string | undefined
): Promise<void> {
  await updateInferenceProviderSetting(workspaceRoot, providerId, "ApiKey", apiKey);
}

async function updateInferenceProviderSetting(
  workspaceRoot: string,
  providerId: string,
  propertyName: string,
  value: string | number | boolean | undefined
): Promise<void> {
  const settingsPaths = getInferenceProviderSettingsPaths(workspaceRoot);
  if (settingsPaths.length === 0) {
    throw new Error("Could not find MCPServer.Host/appsettings.json or the built appsettings copy.");
  }

  for (const settingsPath of settingsPaths) {
    const appSettings = readJsonFile(settingsPath);
    updateProviderSettingInAppSettings(appSettings, providerId, propertyName, value);
    writeJsonFile(settingsPath, appSettings);
  }
}

async function updateInferenceRoutingSetting(
  workspaceRoot: string,
  propertyName: string,
  value: string | number | boolean | undefined
): Promise<void> {
  const settingsPaths = getInferenceProviderSettingsPaths(workspaceRoot);
  if (settingsPaths.length === 0) {
    throw new Error("Could not find MCPServer.Host/appsettings.json or the built appsettings copy.");
  }

  for (const settingsPath of settingsPaths) {
    const appSettings = readJsonFile(settingsPath);
    updateRoutingSettingInAppSettings(appSettings, propertyName, value);
    writeJsonFile(settingsPath, appSettings);
  }
}

function updateProviderSettingInAppSettings(
  appSettings: unknown,
  providerId: string,
  propertyName: string,
  value: string | number | boolean | undefined
): void {
  if (!isRecord(appSettings)) {
    throw new Error("The appsettings.json file must contain a JSON object.");
  }

  const inferenceSection = ensureRecord(appSettings, "McpInference");
  const providersSection = ensureRecord(inferenceSection, "Providers");
  const providerSection = ensureRecord(providersSection, providerId);
  setOptionalProperty(providerSection, propertyName, value);
}

function updateRoutingSettingInAppSettings(
  appSettings: unknown,
  propertyName: string,
  value: string | number | boolean | undefined
): void {
  if (!isRecord(appSettings)) {
    throw new Error("The appsettings.json file must contain a JSON object.");
  }

  const inferenceSection = ensureRecord(appSettings, "McpInference");
  const routingSection = ensureRecord(inferenceSection, "Routing");
  setOptionalProperty(routingSection, propertyName, value);
}

function readInferenceProviders(workspaceRoot: string): InferenceProviderConfig[] {
  const settingsPath = getSourceInferenceProviderSettingsPath(workspaceRoot);
  if (!existsSync(settingsPath)) {
    return [];
  }

  const appSettings = readJsonFile(settingsPath);
  if (!isRecord(appSettings)) {
    return [];
  }

  const inferenceSection = appSettings.McpInference;
  if (!isRecord(inferenceSection)) {
    return [];
  }

  const providersSection = inferenceSection.Providers;
  if (!isRecord(providersSection)) {
    return [];
  }

  return Object.entries(providersSection)
    .map(([providerId, rawProvider]) => {
      if (!isRecord(rawProvider)) {
        return undefined;
      }

      return {
        providerId,
        enabled: readBoolean(rawProvider.Enabled),
        baseAddress: readString(rawProvider.BaseAddress),
        model: readString(rawProvider.Model),
        httpClientName: readString(rawProvider.HttpClientName),
        apiKey: readString(rawProvider.ApiKey),
        routingPriority: readOptionalInt(rawProvider.RoutingPriority) ?? 0,
        maxTokens: readOptionalInt(rawProvider.MaxTokens),
        temperature: readOptionalNumber(rawProvider.Temperature),
        topP: readOptionalNumber(rawProvider.TopP),
        topK: readOptionalInt(rawProvider.TopK),
        repeatPenalty: readOptionalNumber(rawProvider.RepeatPenalty),
        seed: readOptionalInt(rawProvider.Seed),
        contextLength: readOptionalInt(rawProvider.ContextLength),
        keepAlive: readString(rawProvider.KeepAlive)
      } satisfies InferenceProviderConfig;
    })
    .filter((provider): provider is InferenceProviderConfig => provider !== undefined)
    .sort((left, right) => {
      if (left.routingPriority !== right.routingPriority) {
        return left.routingPriority - right.routingPriority;
      }

      return left.providerId.localeCompare(right.providerId);
    });
}

function readInferenceRoutingSettings(workspaceRoot: string): InferenceRoutingConfig {
  const settingsPath = getSourceInferenceProviderSettingsPath(workspaceRoot);
  if (!existsSync(settingsPath)) {
    return {
      defaultStrategy: "PrimaryThenFallback",
      tandemCandidateCount: 2,
      tandemValidationEnabled: false,
      tandemValidationProviderId: "",
      tandemValidationModel: ""
    };
  }

  const appSettings = readJsonFile(settingsPath);
  if (!isRecord(appSettings)) {
    return {
      defaultStrategy: "PrimaryThenFallback",
      tandemCandidateCount: 2,
      tandemValidationEnabled: false,
      tandemValidationProviderId: "",
      tandemValidationModel: ""
    };
  }

  const inferenceSection = appSettings.McpInference;
  if (!isRecord(inferenceSection)) {
    return {
      defaultStrategy: "PrimaryThenFallback",
      tandemCandidateCount: 2,
      tandemValidationEnabled: false,
      tandemValidationProviderId: "",
      tandemValidationModel: ""
    };
  }

  const routingSection = inferenceSection.Routing;
  if (!isRecord(routingSection)) {
    return {
      defaultStrategy: "PrimaryThenFallback",
      tandemCandidateCount: 2,
      tandemValidationEnabled: false,
      tandemValidationProviderId: "",
      tandemValidationModel: ""
    };
  }

  return {
    defaultStrategy: readString(routingSection.DefaultStrategy) || "PrimaryThenFallback",
    tandemCandidateCount: readOptionalInt(routingSection.TandemCandidateCount) ?? 2,
    tandemValidationEnabled: readBoolean(routingSection.TandemValidationEnabled),
    tandemValidationProviderId: readString(routingSection.TandemValidationProviderId),
    tandemValidationModel: readString(routingSection.TandemValidationModel)
  };
}

function getInferenceProviderTreeItems(workspaceRoot: string): WorkspaceTreeItem[] {
  const providers = readInferenceProviders(workspaceRoot);
  if (providers.length === 0) {
    return [
      new WorkspaceTreeItem(
        "No inference providers configured",
        vscode.TreeItemCollapsibleState.None,
        new vscode.ThemeIcon("warning"),
        "Open MCPServer.Host/appsettings.json and configure McpInference:Providers",
        undefined,
        "inference-provider-empty"
      )
    ];
  }

  return providers.map((provider) => {
    const description = provider.model || "provider default";
    const tooltipLines = [
      `Provider: ${provider.providerId}`,
      `Enabled: ${provider.enabled ? "yes" : "no"}`,
      `Routing priority: ${provider.routingPriority}`,
      `Model: ${description}`,
      `Base address: ${provider.baseAddress || "not configured"}`,
      `HTTP client: ${provider.httpClientName || "not configured"}`,
      `Credentials: ${provider.apiKey ? "configured" : "not configured"}`,
      `Max tokens: ${formatOptionalNumber(provider.maxTokens)}`,
      `Temperature: ${formatOptionalNumber(provider.temperature)}`,
      `Top p: ${formatOptionalNumber(provider.topP)}`,
      `Top k: ${formatOptionalNumber(provider.topK)}`,
      `Repeat penalty: ${formatOptionalNumber(provider.repeatPenalty)}`,
      `Seed: ${formatOptionalNumber(provider.seed)}`,
      `Context length: ${formatOptionalNumber(provider.contextLength)}`,
      `Keep alive: ${provider.keepAlive || "default"}`
    ];

    return new WorkspaceTreeItem(
      provider.providerId,
      vscode.TreeItemCollapsibleState.None,
      new vscode.ThemeIcon(provider.enabled ? "symbol-parameter" : "circle-slash"),
      description,
      {
        command: CONFIGURE_INFERENCE_PROVIDER_MODEL_COMMAND,
        title: "Choose inference model",
        arguments: [provider.providerId]
      },
      "inference-provider",
      tooltipLines.join("\n")
    );
  });
}

function getSourceInferenceProviderSettingsPath(workspaceRoot: string): string {
  return path.join(workspaceRoot, HOST_PROJECT, HOST_APPSETTINGS_FILE);
}

function getBuiltInferenceProviderSettingsPath(workspaceRoot: string): string {
  return path.join(workspaceRoot, HOST_PROJECT, "bin", DEBUG_OUTPUT_CONFIGURATION, TARGET_FRAMEWORK, HOST_APPSETTINGS_FILE);
}

function getInferenceProviderSettingsPaths(workspaceRoot: string): string[] {
  const paths = [
    getSourceInferenceProviderSettingsPath(workspaceRoot),
    getBuiltInferenceProviderSettingsPath(workspaceRoot)
  ];

  return paths.filter((settingsPath, index) => existsSync(settingsPath) && paths.indexOf(settingsPath) === index);
}

function buildLiveInferenceModelsUrl(provider: InferenceProviderConfig): URL | undefined {
  const baseAddress = normalizeProviderBaseAddress(provider.providerId, provider.baseAddress);
  if (stringIsBlank(baseAddress)) {
    return undefined;
  }

  return new URL(getInferenceProviderModelsPath(provider.providerId), ensureTrailingSlash(baseAddress));
}

function normalizeProviderBaseAddress(providerId: string, baseAddress: string): string {
  const normalizedProviderId = providerId.trim().toLowerCase();
  const normalizedBaseAddress = baseAddress.trim();
  if (normalizedProviderId === "ollama" && normalizedBaseAddress.toLowerCase().endsWith("/v1/")) {
    return normalizedBaseAddress.slice(0, -4);
  }

  if (normalizedProviderId === "ollama" && normalizedBaseAddress.toLowerCase().endsWith("/v1")) {
    return normalizedBaseAddress.slice(0, -3);
  }

  return normalizedBaseAddress;
}

function getInferenceProviderModelsPath(providerId: string): string {
  switch (providerId.trim().toLowerCase()) {
    case "ollama":
      return "api/tags";
    default:
      return "models";
  }
}

function setOptionalProperty(
  target: Record<string, unknown>,
  propertyName: string,
  value: string | number | boolean | undefined
): void {
  if (value === undefined) {
    delete target[propertyName];
    return;
  }

  target[propertyName] = value;
}

function readOptionalInt(value: unknown): number | undefined {
  if (typeof value === "number" && Number.isInteger(value)) {
    return value;
  }

  if (typeof value === "string") {
    const parsedValue = Number.parseInt(value.trim(), 10);
    return Number.isNaN(parsedValue) ? undefined : parsedValue;
  }

  return undefined;
}

function readOptionalNumber(value: unknown): number | undefined {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === "string") {
    const parsedValue = Number.parseFloat(value.trim());
    return Number.isNaN(parsedValue) ? undefined : parsedValue;
  }

  return undefined;
}

function formatOptionalNumber(value: number | undefined): string {
  return value === undefined ? "default" : `${value}`;
}

function formatBytes(value: number | undefined): string | undefined {
  if (value === undefined || !Number.isFinite(value) || value < 0) {
    return undefined;
  }

  const units = ["B", "KB", "MB", "GB", "TB"];
  let amount = value;
  let unitIndex = 0;
  while (amount >= 1024 && unitIndex < units.length - 1) {
    amount /= 1024;
    unitIndex++;
  }

  const formatted = amount >= 10 || unitIndex === 0
    ? amount.toFixed(0)
    : amount.toFixed(1);

  return `${formatted} ${units[unitIndex]}`;
}

async function configureDefaultRoutingStrategy(workspaceRoot: string): Promise<void> {
  const selectedStrategy = await vscode.window.showQuickPick<RoutingStrategyQuickPickItem>(
    [
      {
        label: "PrimaryThenFallback",
        description: "Try providers in order until one succeeds",
        value: "PrimaryThenFallback"
      },
      {
        label: "PrimaryOnly",
        description: "Use only the first enabled provider",
        value: "PrimaryOnly"
      },
      {
        label: "FanOutCompare",
        description: "Ask multiple providers and stop on first success",
        value: "FanOutCompare"
      },
      {
        label: "TandemValidate",
        description: "Run multiple providers and optionally validate with a dealbreaker model",
        value: "TandemValidate"
      },
      {
        label: "SecondOpinion",
        description: "Run a reviewer model after the primary response",
        value: "SecondOpinion"
      }
    ],
    {
      title: "Default routing strategy",
      placeHolder: "Choose the default inference routing strategy",
      matchOnDescription: true
    }
  );

  if (selectedStrategy === undefined) {
    return;
  }

  await updateInferenceRoutingSetting(workspaceRoot, "DefaultStrategy", selectedStrategy.value);
}

async function configureTandemValidationEnabled(workspaceRoot: string): Promise<void> {
  const selectedValue = await vscode.window.showQuickPick<BooleanQuickPickItem>(
    [
      {
        label: "Enabled",
        value: true
      },
      {
        label: "Disabled",
        value: false
      }
    ],
    {
      title: "Tandem validation",
      placeHolder: "Turn tandem validation on or off"
    }
  );

  if (selectedValue === undefined) {
    return;
  }

  await updateInferenceRoutingSetting(workspaceRoot, "TandemValidationEnabled", selectedValue.value);
}

async function configureTandemCandidateCount(workspaceRoot: string): Promise<void> {
  const routingSettings = readInferenceRoutingSettings(workspaceRoot);
  const candidateCount = await promptForIntegerValue(
    "Tandem candidate count",
    "How many providers should run in tandem? Minimum is 2.",
    routingSettings.tandemCandidateCount
  );

  if (candidateCount.cancelled) {
    return;
  }

  if (candidateCount.value === undefined) {
    await updateInferenceRoutingSetting(workspaceRoot, "TandemCandidateCount", undefined);
    return;
  }

  await updateInferenceRoutingSetting(workspaceRoot, "TandemCandidateCount", Math.max(2, candidateCount.value as number));
}

async function configureTandemValidationProvider(workspaceRoot: string): Promise<void> {
  const providers = readInferenceProviders(workspaceRoot);
  if (providers.length === 0) {
    await vscode.window.showWarningMessage("No inference providers were found in MCPServer.Host/appsettings.json.");
    return;
  }

  const selectedProvider = await selectInferenceProvider(providers);
  if (selectedProvider === undefined) {
    return;
  }

  await updateInferenceRoutingSetting(workspaceRoot, "TandemValidationProviderId", selectedProvider.providerId);
}

async function configureTandemValidationModel(workspaceRoot: string): Promise<void> {
  const routingSettings = readInferenceRoutingSettings(workspaceRoot);
  const providers = readInferenceProviders(workspaceRoot);
  if (providers.length === 0) {
    await vscode.window.showWarningMessage("No inference providers were found in MCPServer.Host/appsettings.json.");
    return;
  }

  const selectedProviderId = routingSettings.tandemValidationProviderId.length > 0
    ? routingSettings.tandemValidationProviderId
    : undefined;
  const selectedProvider = selectedProviderId === undefined
    ? await selectInferenceProvider(providers)
    : providers.find((candidate) => candidate.providerId === selectedProviderId);

  if (selectedProvider === undefined) {
    return;
  }

  const selectedModel = await selectInferenceProviderModel(selectedProvider);
  if (selectedModel === undefined) {
    return;
  }

  await updateInferenceRoutingSetting(workspaceRoot, "TandemValidationProviderId", selectedProvider.providerId);
  await updateInferenceRoutingSetting(workspaceRoot, "TandemValidationModel", selectedModel);
}

function getPerformanceSettingItems(provider: InferenceProviderConfig): ProviderPerformanceSettingItem[] {
  const commonSettings: ProviderPerformanceSettingItem[] = [
    {
      label: "Max tokens",
      description: formatOptionalNumber(provider.maxTokens),
      propertyName: "MaxTokens",
      prompt: async selectedProvider => promptForIntegerValue(
        `Max tokens for ${selectedProvider.providerId}`,
        "Maximum number of output tokens. Leave blank to clear it.",
        selectedProvider.maxTokens
      )
    },
    {
      label: "Temperature",
      description: formatOptionalNumber(provider.temperature),
      propertyName: "Temperature",
      prompt: async selectedProvider => promptForNumberValue(
        `Temperature for ${selectedProvider.providerId}`,
        "Randomness in token selection. Leave blank to clear it.",
        selectedProvider.temperature
      )
    },
    {
      label: "Top p",
      description: formatOptionalNumber(provider.topP),
      propertyName: "TopP",
      prompt: async selectedProvider => promptForNumberValue(
        `Top p for ${selectedProvider.providerId}`,
        "Minimum cumulative probability for the next tokens. Leave blank to clear it.",
        selectedProvider.topP
      )
    },
    {
      label: "Seed",
      description: formatOptionalNumber(provider.seed),
      propertyName: "Seed",
      prompt: async selectedProvider => promptForIntegerValue(
        `Seed for ${selectedProvider.providerId}`,
        "Set a seed for deterministic output. Leave blank to clear it.",
        selectedProvider.seed
      )
    }
  ];

  if (provider.providerId.toLowerCase() !== "lmstudio" && provider.providerId.toLowerCase() !== "ollama") {
    return commonSettings;
  }

  const advancedLocalSettings: ProviderPerformanceSettingItem[] = [
    {
      label: "Top k",
      description: formatOptionalNumber(provider.topK),
      propertyName: "TopK",
      prompt: async selectedProvider => promptForIntegerValue(
        `Top k for ${selectedProvider.providerId}`,
        "Limit next token selection to the top-k candidates. Leave blank to clear it.",
        selectedProvider.topK
      )
    },
    {
      label: "Repeat penalty",
      description: formatOptionalNumber(provider.repeatPenalty),
      propertyName: "RepeatPenalty",
      prompt: async selectedProvider => promptForNumberValue(
        `Repeat penalty for ${selectedProvider.providerId}`,
        "Penalize repeated token sequences. Leave blank to clear it.",
        selectedProvider.repeatPenalty
      )
    }
  ];

  if (provider.providerId.toLowerCase() === "ollama") {
    advancedLocalSettings.push(
      {
        label: "Context length",
        description: formatOptionalNumber(provider.contextLength),
        propertyName: "ContextLength",
        prompt: async selectedProvider => promptForIntegerValue(
          `Context length for ${selectedProvider.providerId}`,
          "Number of tokens to consider as context. Leave blank to clear it.",
          selectedProvider.contextLength
        )
      },
      {
        label: "Keep alive",
        description: provider.keepAlive || "default",
        propertyName: "KeepAlive",
        prompt: async selectedProvider => promptForStringValue(
          `Keep alive for ${selectedProvider.providerId}`,
          "Model keep-alive duration, for example 5m or 0. Leave blank to clear it.",
          selectedProvider.keepAlive
        )
      }
    );
  }

  return [...commonSettings, ...advancedLocalSettings];
}

async function promptForIntegerValue(
  title: string,
  prompt: string,
  currentValue: number | undefined
): Promise<ProviderPerformancePromptResult> {
  const input = await vscode.window.showInputBox({
    title,
    prompt,
    value: currentValue === undefined ? "" : `${currentValue}`,
    ignoreFocusOut: true
  });

  if (input === undefined) {
    return {
      cancelled: true,
      value: undefined
    };
  }

  const trimmed = input.trim();
  if (trimmed.length === 0 || trimmed.toLowerCase() === "clear") {
    return {
      cancelled: false,
      value: undefined
    };
  }

  const parsed = Number.parseInt(trimmed, 10);
  if (Number.isNaN(parsed)) {
    await vscode.window.showWarningMessage(`Invalid integer: ${input}`);
    return promptForIntegerValue(title, prompt, currentValue);
  }

  return {
    cancelled: false,
    value: parsed
  };
}

async function promptForNumberValue(
  title: string,
  prompt: string,
  currentValue: number | undefined
): Promise<ProviderPerformancePromptResult> {
  const input = await vscode.window.showInputBox({
    title,
    prompt,
    value: currentValue === undefined ? "" : `${currentValue}`,
    ignoreFocusOut: true
  });

  if (input === undefined) {
    return {
      cancelled: true,
      value: undefined
    };
  }

  const trimmed = input.trim();
  if (trimmed.length === 0 || trimmed.toLowerCase() === "clear") {
    return {
      cancelled: false,
      value: undefined
    };
  }

  const parsed = Number.parseFloat(trimmed);
  if (Number.isNaN(parsed)) {
    await vscode.window.showWarningMessage(`Invalid number: ${input}`);
    return promptForNumberValue(title, prompt, currentValue);
  }

  return {
    cancelled: false,
    value: parsed
  };
}

async function promptForStringValue(
  title: string,
  prompt: string,
  currentValue: string
): Promise<ProviderPerformancePromptResult> {
  const input = await vscode.window.showInputBox({
    title,
    prompt,
    value: currentValue,
    ignoreFocusOut: true
  });

  if (input === undefined) {
    return {
      cancelled: true,
      value: undefined
    };
  }

  const trimmed = input.trim();
  if (trimmed.length === 0 || trimmed.toLowerCase() === "clear") {
    return {
      cancelled: false,
      value: undefined
    };
  }

  return {
    cancelled: false,
    value: trimmed
  };
}

function readJsonFile(filePath: string): unknown {
  return JSON.parse(readFileSync(filePath, "utf8")) as unknown;
}

function writeJsonFile(filePath: string, value: unknown): void {
  writeFileSync(filePath, `${JSON.stringify(value, undefined, 2)}\n`, "utf8");
}

function ensureRecord(target: Record<string, unknown>, propertyName: string): Record<string, unknown> {
  const value = target[propertyName];
  if (isRecord(value)) {
    return value;
  }

  const created: Record<string, unknown> = {};
  target[propertyName] = created;
  return created;
}

function readString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function readBoolean(value: unknown): boolean {
  return typeof value === "boolean" ? value : false;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function stringIsBlank(value: string | undefined): boolean {
  return value === undefined || value.trim().length === 0;
}

function ensureTrailingSlash(value: string): string {
  return value.endsWith("/") ? value : `${value}/`;
}

function refreshWorkspacePresentation(
  provider: McpServerWorkspaceViewProvider,
  statusBarItem: vscode.StatusBarItem
): void {
  provider.refresh();
  updateWorkspaceStatusBar(statusBarItem);
}

async function refreshMcpServerState(
  provider: McpServerWorkspaceViewProvider,
  statusBarItem: vscode.StatusBarItem
): Promise<void> {
  refreshWorkspacePresentation(provider, statusBarItem);
  await vscode.window.showInformationMessage("Refreshed the MCPServer workspace view.");
}

function updateWorkspaceStatusBar(statusBarItem: vscode.StatusBarItem): void {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    statusBarItem.text = "$(warning) MCPServer: open repo";
    statusBarItem.tooltip = new vscode.MarkdownString(
      "Open the MCPServer repository folder to activate the workspace bubble."
    );
    statusBarItem.show();
    return;
  }

  statusBarItem.text = `$(rocket) MCPServer: ${getWorkspaceLabel()}`;
  statusBarItem.tooltip = new vscode.MarkdownString(
    `**MCPServer workspace**\n\nRoot: \`${workspaceRoot}\`\n\nClick for workspace actions, chat, model selection, and routing controls.`
  );
  statusBarItem.show();
}

async function showFirstRunUi(
  context: vscode.ExtensionContext,
  chatTerminalManager: ChatTerminalManager
): Promise<void> {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    return;
  }

  const uiAlreadyOpened = context.globalState.get<boolean>(UI_AUTO_OPENED_KEY, false);
  if (uiAlreadyOpened) {
    return;
  }

  await context.globalState.update(UI_AUTO_OPENED_KEY, true);

  try {
    await vscode.commands.executeCommand("workbench.view.extension.mcpserver");
  } catch {
    // If the shell doesn't recognize the container command yet, keep going.
  }

  const selection = await vscode.window.showInformationMessage(
    "MCPServer UI is ready. I opened the sidebar so the workspace and inference controls are visible.",
    "Open Workspace Actions",
    "Open Chat Console"
  );

  if (selection === "Open Workspace Actions") {
    await vscode.commands.executeCommand(WORKSPACE_STATUS_COMMAND);
    return;
  }

  if (selection === "Open Chat Console") {
    await chatTerminalManager.openOrRevealAsync();
  }
}

function getPrimaryWorkspaceFolder(): vscode.WorkspaceFolder | undefined {
  const activeEditor = vscode.window.activeTextEditor;
  if (activeEditor !== undefined) {
    const folderFromActiveEditor = vscode.workspace.getWorkspaceFolder(activeEditor.document.uri);
    if (folderFromActiveEditor !== undefined) {
      return folderFromActiveEditor;
    }
  }

  return vscode.workspace.workspaceFolders?.[0];
}

function requireWorkspaceRoot(): string | undefined {
  const workspaceRoot = getWorkspaceRoot();
  if (workspaceRoot === undefined) {
    void vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
    return undefined;
  }

  return workspaceRoot;
}

function getWorkspaceRoot(): string | undefined {
  return getPrimaryWorkspaceFolder()?.uri.fsPath;
}

function getWorkspaceLabel(): string {
  const workspaceFolder = getPrimaryWorkspaceFolder();
  if (workspaceFolder === undefined) {
    return "no workspace";
  }

  return workspaceFolder.name || path.basename(workspaceFolder.uri.fsPath);
}

function getProjectOutputPath(workspaceRoot: string, projectName: string, fileName: string): string {
  return path.join(workspaceRoot, projectName, "bin", DEBUG_OUTPUT_CONFIGURATION, TARGET_FRAMEWORK, fileName);
}

function quoteForShell(value: string): string {
  return `"${value.replaceAll('"', '\\"')}"`;
}

function requireBuiltArtifacts(pathsToCheck: readonly string[]): boolean {
  const missingPaths = pathsToCheck.filter((artifactPath) => !existsSync(artifactPath));
  if (missingPaths.length === 0) {
    return true;
  }

  void vscode.window.showWarningMessage(
    `Build the MCPServer launcher first. Missing artifact: ${path.basename(missingPaths[0])}`
  );
  return false;
}

class ChatTerminalManager {
  private terminal: vscode.Terminal | undefined;

  public async openOrRevealAsync(): Promise<void> {
    const workspaceRoot = requireWorkspaceRoot();
    if (workspaceRoot === undefined) {
      return;
    }

    const launcherDll = getProjectOutputPath(workspaceRoot, CHAT_LAUNCHER_PROJECT, `${CHAT_LAUNCHER_PROJECT}.dll`);
    if (!requireBuiltArtifacts([launcherDll])) {
      return;
    }

    if (this.terminal !== undefined) {
      this.terminal.show(true);
      return;
    }

    this.terminal = vscode.window.createTerminal({ name: "MCPServer Chat Console", cwd: workspaceRoot });
    this.terminal.show(true);
    this.terminal.sendText(`dotnet exec ${quoteForShell(launcherDll)}`, true);
  }

  public handleTerminalClosed(closedTerminal: vscode.Terminal): void {
    if (this.terminal === closedTerminal) {
      this.terminal = undefined;
    }
  }
}

class McpServerWorkspaceViewProvider implements vscode.TreeDataProvider<WorkspaceTreeItem> {
  private readonly _onDidChangeTreeData = new vscode.EventEmitter<WorkspaceTreeItem | undefined>();

  public readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  public refresh(): void {
    this._onDidChangeTreeData.fire(undefined);
  }

  public getTreeItem(element: WorkspaceTreeItem): vscode.TreeItem {
    return element;
  }

  public getChildren(element?: WorkspaceTreeItem): WorkspaceTreeItem[] {
    if (element?.contextValue === "inference-providers") {
      const workspaceRoot = getWorkspaceRoot();
      return workspaceRoot === undefined ? [] : getInferenceProviderTreeItems(workspaceRoot);
    }

    if (element !== undefined) {
      return [];
    }

    const workspaceRoot = getWorkspaceRoot();
    const workspaceLabel = getWorkspaceLabel();

    if (workspaceRoot === undefined) {
      return [
        new WorkspaceTreeItem(
          "Open the MCPServer repository folder first.",
          vscode.TreeItemCollapsibleState.None,
          new vscode.ThemeIcon("warning"),
          undefined,
          {
            command: "mcpserver.showWorkspaceActions",
            title: "Open workspace actions"
          },
          "workspace-status"
        ),
      ];
    }

    return [
      new WorkspaceTreeItem(
        "Open MCPServer Actions",
        vscode.TreeItemCollapsibleState.None,
        new vscode.ThemeIcon("lightbulb"),
        "Launch chat, pick models, tune routing, or update provider settings",
        {
          command: "mcpserver.showWorkspaceActions",
          title: "Open MCPServer actions"
        },
        "action"
      ),
      new WorkspaceTreeItem(
        workspaceLabel,
        vscode.TreeItemCollapsibleState.None,
        new vscode.ThemeIcon("folder-active"),
        workspaceRoot,
        {
          command: "mcpserver.showWorkspaceRoot",
          title: "Show workspace root"
        },
        "workspace-root"
      ),
      new WorkspaceTreeItem(
        "Open Chat Console",
        vscode.TreeItemCollapsibleState.None,
        new vscode.ThemeIcon("comment-discussion"),
        "Launch the repo-owned chat REPL",
        {
          command: "mcpserver.openChatConsole",
          title: "Open chat console"
        },
        "action"
      ),
      new WorkspaceTreeItem(
        "Workspace Root Actions",
        vscode.TreeItemCollapsibleState.None,
        new vscode.ThemeIcon("layers"),
        "Copy root, open Explorer, or refresh MCPServer state",
        {
          command: "mcpserver.showWorkspaceActions",
          title: "Show workspace actions"
        },
        "action"
      ),
      new WorkspaceTreeItem(
        "Inference Routing",
        vscode.TreeItemCollapsibleState.None,
        new vscode.ThemeIcon("git-branch"),
        "Tune tandem validation, second opinion flow, and provider priority",
        {
          command: CONFIGURE_INFERENCE_ROUTING_COMMAND,
          title: "Configure inference routing"
        },
        "action"
      ),
      new WorkspaceTreeItem(
        "Inference Providers",
        vscode.TreeItemCollapsibleState.Collapsed,
        new vscode.ThemeIcon("server"),
        "Choose the live model for each configured inference provider",
        undefined,
        "inference-providers"
      ),
      new WorkspaceTreeItem(
        "Refresh MCPServer State",
        vscode.TreeItemCollapsibleState.None,
        new vscode.ThemeIcon("refresh"),
        "Re-read workspace state and refresh the MCPServer sidebar",
        {
          command: "mcpserver.refreshMcpServerState",
          title: "Refresh MCPServer state"
        },
        "action"
      )
    ];
  }
}

class WorkspaceTreeItem extends vscode.TreeItem {
  public constructor(
    label: string,
    collapsibleState: vscode.TreeItemCollapsibleState,
    iconPath: vscode.ThemeIcon,
    description: string | undefined,
    command: vscode.Command | undefined,
    public readonly contextValue: string,
    tooltip?: string
  ) {
    super(label, collapsibleState);
    this.iconPath = iconPath;
    this.description = description;
    this.command = command;
    this.tooltip = tooltip;
  }
}

interface WorkspaceActionItem extends vscode.QuickPickItem {
  readonly action: () => Promise<void>;
}

interface RoutingStrategyQuickPickItem extends vscode.QuickPickItem {
  readonly value: string;
}

interface BooleanQuickPickItem extends vscode.QuickPickItem {
  readonly value: boolean;
}

interface ProviderPerformanceQuickPickItem extends vscode.QuickPickItem {
  readonly field: ProviderPerformanceSettingItem;
}

interface InferenceProviderConfig {
  readonly providerId: string;
  readonly enabled: boolean;
  readonly baseAddress: string;
  readonly model: string;
  readonly httpClientName: string;
  readonly apiKey: string;
  readonly routingPriority: number;
  readonly maxTokens: number | undefined;
  readonly temperature: number | undefined;
  readonly topP: number | undefined;
  readonly topK: number | undefined;
  readonly repeatPenalty: number | undefined;
  readonly seed: number | undefined;
  readonly contextLength: number | undefined;
  readonly keepAlive: string;
}

interface InferenceRoutingConfig {
  readonly defaultStrategy: string;
  readonly tandemCandidateCount: number;
  readonly tandemValidationEnabled: boolean;
  readonly tandemValidationProviderId: string;
  readonly tandemValidationModel: string;
}

interface ProviderPerformanceSettingItem {
  readonly label: string;
  readonly description: string;
  readonly propertyName: string;
  readonly prompt: (provider: InferenceProviderConfig) => Promise<ProviderPerformancePromptResult>;
}

interface ProviderPerformancePromptResult {
  readonly cancelled: boolean;
  readonly value: string | number | boolean | undefined;
}

interface LiveInferenceModel {
  readonly identifier: string;
  readonly label: string;
  readonly detail: string | undefined;
}
