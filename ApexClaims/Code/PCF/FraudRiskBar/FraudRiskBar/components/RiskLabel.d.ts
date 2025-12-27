import * as React from "react";
import { RiskLevel } from "../utils/riskLevels";
export interface RiskLabelProps {
    riskLevel: RiskLevel | null;
    visible: boolean;
}
export declare const RiskLabel: React.FC<RiskLabelProps>;
