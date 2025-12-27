export interface RiskLevel {
    label: string;
    primaryColor: string;
    backgroundColor: string;
    minScore: number;
    maxScore: number;
}

export const RISK_LEVELS: RiskLevel[] = [
    {
        label: "Low Risk",
        primaryColor: "#22C55E",
        backgroundColor: "#DCFCE7",
        minScore: 0,
        maxScore: 25
    },
    {
        label: "Medium Risk",
        primaryColor: "#EAB308",
        backgroundColor: "#FEF9C3",
        minScore: 26,
        maxScore: 50
    },
    {
        label: "High Risk",
        primaryColor: "#F97316",
        backgroundColor: "#FFEDD5",
        minScore: 51,
        maxScore: 75
    },
    {
        label: "Critical Risk",
        primaryColor: "#EF4444",
        backgroundColor: "#FEE2E2",
        minScore: 76,
        maxScore: 100
    }
];

export const COLOR_ZONES = {
    green: "#22C55E",
    yellow: "#EAB308",
    orange: "#F97316",
    red: "#EF4444"
};

export const TICK_MARKS = [0, 25, 50, 75, 100];

/**
 * Clamps a score to the valid range of 0-100
 */
export function clampScore(score: number | null | undefined): number | null {
    if (score === null || score === undefined) {
        return null;
    }
    const rounded = Math.round(score);
    return Math.max(0, Math.min(100, rounded));
}

/**
 * Gets the risk level for a given score
 */
export function getRiskLevel(score: number | null): RiskLevel | null {
    if (score === null) {
        return null;
    }

    for (const level of RISK_LEVELS) {
        if (score >= level.minScore && score <= level.maxScore) {
            return level;
        }
    }

    // Default to last level if score is out of expected range
    return RISK_LEVELS[RISK_LEVELS.length - 1];
}

/**
 * Gets the fill color based on score position
 */
export function getFillColor(score: number): string {
    if (score <= 25) {
        return COLOR_ZONES.green;
    } else if (score <= 50) {
        return COLOR_ZONES.yellow;
    } else if (score <= 75) {
        return COLOR_ZONES.orange;
    } else {
        return COLOR_ZONES.red;
    }
}

/**
 * Checks if score is in critical range
 */
export function isCritical(score: number | null): boolean {
    return score !== null && score > 75;
}

/**
 * Gets the accessibility label for a score
 */
export function getAriaLabel(score: number | null): string {
    if (score === null) {
        return "Fraud Risk Score: No score available";
    }

    const level = getRiskLevel(score);
    return `Fraud Risk Score: ${score} - ${level?.label || "Unknown"}`;
}
