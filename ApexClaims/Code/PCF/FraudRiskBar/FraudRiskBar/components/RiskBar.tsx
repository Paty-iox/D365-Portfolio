import * as React from "react";
import { RiskLabel } from "./RiskLabel";
import {
    clampScore,
    getRiskLevel,
    getFillColor,
    isCritical,
    getAriaLabel,
    TICK_MARKS
} from "../utils/riskLevels";

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

export const RiskBar: React.FC<RiskBarProps> = ({
    score,
    showLabel,
    showTicks,
    showHeader,
    enableAnimation,
    enablePulse,
    barHeight,
    disabled = false
}) => {
    const [displayScore, setDisplayScore] = React.useState<number | null>(null);
    const [animatedWidth, setAnimatedWidth] = React.useState<number>(0);
    const [containerWidth, setContainerWidth] = React.useState<number>(400);
    const containerRef = React.useRef<HTMLDivElement>(null);

    const clampedScore = clampScore(score);
    const riskLevel = getRiskLevel(clampedScore);
    const fillColor = clampedScore !== null ? getFillColor(clampedScore) : "#9CA3AF";
    const showCriticalPulse = enablePulse && isCritical(clampedScore);
    const isMinimal = containerWidth < 200;

    React.useEffect(() => {
        if (!containerRef.current) return;

        const resizeObserver = new ResizeObserver((entries) => {
            for (const entry of entries) {
                setContainerWidth(entry.contentRect.width);
            }
        });

        resizeObserver.observe(containerRef.current);
        return () => resizeObserver.disconnect();
    }, []);

    React.useEffect(() => {
        if (clampedScore === null) {
            setDisplayScore(null);
            setAnimatedWidth(0);
            return;
        }

        setDisplayScore(clampedScore);

        if (!enableAnimation) {
            setAnimatedWidth(clampedScore);
            return;
        }

        const startWidth = animatedWidth;
        const duration = 200;
        const startTime = performance.now();
        let animationId: number;

        const animate = (currentTime: number) => {
            const elapsed = currentTime - startTime;
            const progress = Math.min(elapsed / duration, 1);

            // Ease-out cubic
            const easeOut = 1 - Math.pow(1 - progress, 3);
            const currentWidth = startWidth + (clampedScore - startWidth) * easeOut;

            setAnimatedWidth(currentWidth);

            if (progress < 1) {
                animationId = requestAnimationFrame(animate);
            }
        };

        animationId = requestAnimationFrame(animate);

        return () => {
            if (animationId) cancelAnimationFrame(animationId);
        };
    }, [clampedScore, enableAnimation]);

    const trackBackground = "#E5E7EB";

    const containerStyle: React.CSSProperties = {
        width: "100%",
        maxWidth: "450px",
        minWidth: "150px",
        fontFamily: "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif",
        opacity: disabled ? 0.5 : 1,
        pointerEvents: disabled ? "none" : "auto"
    };

    const scoreDisplayStyle: React.CSSProperties = {
        fontSize: "18px",
        fontWeight: 700,
        color: fillColor
    };

    const trackStyle: React.CSSProperties = {
        position: "relative",
        width: "100%",
        height: `${barHeight}px`,
        borderRadius: `${barHeight / 2}px`,
        background: trackBackground,
        overflow: "hidden"
    };

    const fillStyle: React.CSSProperties = {
        position: "absolute",
        top: 0,
        left: 0,
        height: "100%",
        width: `${animatedWidth}%`,
        backgroundColor: fillColor,
        borderRadius: `${barHeight / 2}px`,
        transition: enableAnimation ? "background-color 0.15s ease-out" : "width 0.3s ease-out, background-color 0.15s ease-out"
    };


    const pulseStyle: React.CSSProperties = showCriticalPulse ? {
        animation: "pulse-glow 2s ease-in-out infinite"
    } : {};

    const ticksContainerStyle: React.CSSProperties = {
        position: "relative",
        height: "20px",
        marginTop: "4px"
    };

    const getTickStyle = (tick: number): React.CSSProperties => ({
        position: "absolute",
        left: `${tick}%`,
        transform: "translateX(-50%)",
        display: "flex",
        flexDirection: "column",
        alignItems: "center"
    });

    const tickMarkStyle: React.CSSProperties = {
        width: "1px",
        height: "6px",
        backgroundColor: "#9CA3AF"
    };

    const tickLabelStyle: React.CSSProperties = {
        fontSize: "10px",
        color: "#6B7280",
        marginTop: "2px"
    };

    const noScoreStyle: React.CSSProperties = {
        position: "absolute",
        top: "50%",
        left: "50%",
        transform: "translate(-50%, -50%)",
        fontSize: "12px",
        color: "#6B7280",
        fontWeight: 500
    };

    return (
        <div
            ref={containerRef}
            className={`fraud-risk-bar ${showCriticalPulse ? "critical" : ""}`}
            style={containerStyle}
            role="progressbar"
            aria-valuenow={clampedScore ?? undefined}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-label={getAriaLabel(clampedScore)}
            tabIndex={0}
        >
            {!isMinimal && (showLabel || showHeader) && (
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "6px" }}>
                    {showLabel ? (
                        <RiskLabel riskLevel={riskLevel} visible={clampedScore !== null} />
                    ) : <span />}
                    {showHeader && (
                        <span style={scoreDisplayStyle}>
                            {displayScore !== null ? displayScore : "—"}
                        </span>
                    )}
                </div>
            )}

            <div style={{ ...trackStyle, ...pulseStyle }} className="fraud-risk-track">
                {clampedScore !== null ? (
                    <div style={fillStyle} className="fraud-risk-fill" />
                ) : (
                    <span style={noScoreStyle}>No Score</span>
                )}
            </div>

            {showTicks && !isMinimal && (
                <div style={ticksContainerStyle}>
                    {TICK_MARKS.map((tick) => (
                        <div key={tick} style={getTickStyle(tick)}>
                            <div style={tickMarkStyle} />
                            <span style={tickLabelStyle}>{tick}</span>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
};
