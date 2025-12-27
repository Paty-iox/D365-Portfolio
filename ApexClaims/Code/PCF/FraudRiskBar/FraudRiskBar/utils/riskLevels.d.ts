export interface RiskLevel {
    label: string;
    primaryColor: string;
    backgroundColor: string;
    minScore: number;
    maxScore: number;
}
export declare const RISK_LEVELS: RiskLevel[];
export declare const COLOR_ZONES: {
    green: string;
    yellow: string;
    orange: string;
    red: string;
};
export declare const TICK_MARKS: number[];
/**
 * Clamps a score to the valid range of 0-100
 */
export declare function clampScore(score: number | null | undefined): number | null;
/**
 * Gets the risk level for a given score
 */
export declare function getRiskLevel(score: number | null): RiskLevel | null;
/**
 * Gets the fill color based on score position
 */
export declare function getFillColor(score: number): string;
/**
 * Checks if score is in critical range
 */
export declare function isCritical(score: number | null): boolean;
/**
 * Gets the accessibility label for a score
 */
export declare function getAriaLabel(score: number | null): string;
