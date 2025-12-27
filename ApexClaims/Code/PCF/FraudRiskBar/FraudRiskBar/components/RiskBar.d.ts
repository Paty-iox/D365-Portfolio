import * as React from "react";
export interface RiskBarProps {
    score: number | null;
    showLabel: boolean;
    showTicks: boolean;
    showHeader: boolean;
    enableAnimation: boolean;
    enablePulse: boolean;
    barHeight: number;
    disabled?: boolean;
}
export declare const RiskBar: React.FC<RiskBarProps>;
