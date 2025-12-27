import { IInputs, IOutputs } from "./generated/ManifestTypes";
export declare class FraudRiskBar implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container;
    private notifyOutputChanged;
    private context;
    constructor();
    init(context: ComponentFramework.Context<IInputs>, notifyOutputChanged: () => void, state: ComponentFramework.Dictionary, container: HTMLDivElement): void;
    updateView(context: ComponentFramework.Context<IInputs>): void;
    getOutputs(): IOutputs;
    destroy(): void;
}
