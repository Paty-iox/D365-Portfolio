import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { RiskBar, RiskBarProps } from "./components/RiskBar";

export class FraudRiskBar implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private notifyOutputChanged: () => void;
    private context: ComponentFramework.Context<IInputs>;

    constructor() {
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.container = container;
        this.notifyOutputChanged = notifyOutputChanged;
        this.context = context;

        context.mode.trackContainerResize(true);
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;

        const score = context.parameters.riskScore.raw;
        const showLabel = context.parameters.showLabel?.raw ?? true;
        const showTicks = context.parameters.showTicks?.raw ?? true;
        const showHeader = context.parameters.showHeader?.raw ?? true;
        const enableAnimation = context.parameters.enableAnimation?.raw ?? true;
        const enablePulse = context.parameters.enablePulse?.raw ?? true;
        const barHeight = context.parameters.barHeight?.raw ?? 16;

        const isDisabled = context.mode.isControlDisabled;

        const props: RiskBarProps = {
            score: score,
            showLabel: showLabel,
            showTicks: showTicks,
            showHeader: showHeader,
            enableAnimation: enableAnimation,
            enablePulse: enablePulse,
            barHeight: barHeight,
            disabled: isDisabled
        };

        ReactDOM.render(
            React.createElement(RiskBar, props),
            this.container
        );
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        ReactDOM.unmountComponentAtNode(this.container);
    }
}
