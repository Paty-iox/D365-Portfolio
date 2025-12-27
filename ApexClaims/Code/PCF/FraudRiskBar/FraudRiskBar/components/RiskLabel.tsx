import * as React from "react";
import { RiskLevel } from "../utils/riskLevels";

export interface RiskLabelProps {
    riskLevel: RiskLevel | null;
    visible: boolean;
}

export const RiskLabel: React.FC<RiskLabelProps> = ({ riskLevel, visible }) => {
    if (!visible || !riskLevel) {
        return null;
    }

    const labelStyle: React.CSSProperties = {
        display: "inline-flex",
        alignItems: "center",
        padding: "4px 12px",
        borderRadius: "9999px",
        fontSize: "12px",
        fontWeight: 600,
        color: riskLevel.primaryColor,
        backgroundColor: riskLevel.backgroundColor,
        border: `1px solid ${riskLevel.primaryColor}20`
    };

    return (
        <div className="fraud-risk-label-container">
            <span style={labelStyle} role="status" aria-live="polite">
                {riskLevel.label}
            </span>
        </div>
    );
};
