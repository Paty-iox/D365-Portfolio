// Feedback processing pipeline - triggered when new feedback hits the Service Bus queue
// Runs through language detection, translation, sentiment analysis, and auto-response generation

const https = require('https');
const crypto = require('crypto');

module.exports = async function (context, mySbMsg) {
    context.log('Processing incoming feedback');

    const feedback = typeof mySbMsg === 'string' ? JSON.parse(mySbMsg) : mySbMsg;
    context.log('Processing feedback ID:', feedback.feedbackId || 'N/A');

    const processingLog = {
        startTime: new Date().toISOString(),
        stages: []
    };

    let enrichedFeedback = { ...feedback };
    let textToAnalyze = feedback.feedbackText;

    try {
        // Stage 1 - figure out what language they wrote in
        const languageResult = await detectLanguage(feedback.feedbackText, context);

        enrichedFeedback.detectedLanguage = languageResult.language;
        enrichedFeedback.languageConfidence = languageResult.confidence;
        enrichedFeedback.languageName = languageResult.languageName;

        processingLog.stages.push({
            stage: 1,
            name: 'Language Detection',
            status: 'Success',
            result: languageResult,
            timestamp: new Date().toISOString()
        });

        context.log('Detected Language:', languageResult.languageName, '(', languageResult.language, ')');
        context.log('Confidence:', languageResult.confidence);

        // Stage 2 - translate to English if needed
        context.log('--- Stage 2: Translation ---');

        if (languageResult.language !== 'en' && languageResult.confidence > 0.5) {
            context.log('Translating from', languageResult.language, 'to English...');

            const translationResult = await translateText(feedback.feedbackText, languageResult.language, context);

            enrichedFeedback.originalText = feedback.feedbackText;
            enrichedFeedback.translatedText = translationResult.translatedText;
            textToAnalyze = translationResult.translatedText;

            processingLog.stages.push({
                stage: 2,
                name: 'Translation',
                status: 'Success',
                result: {
                    from: languageResult.language,
                    to: 'en',
                    translated: true
                },
                timestamp: new Date().toISOString()
            });

            context.log('Translation complete');
        } else {
            enrichedFeedback.originalText = feedback.feedbackText;
            enrichedFeedback.translatedText = null;

            processingLog.stages.push({
                stage: 2,
                name: 'Translation',
                status: 'Skipped',
                result: { reason: 'Already in English' },
                timestamp: new Date().toISOString()
            });

            context.log('Skipping translation - already in English');
        }

        // Stage 3 - sentiment analysis via Azure Text Analytics
        context.log('--- Stage 3: Sentiment Analysis ---');
        const sentimentResult = await analyzeSentiment(textToAnalyze, context);

        // Map sentiment to Dataverse option set values
        const categoryMap = {
            'positive': { label: 'Positive', value: 100000000 },
            'neutral': { label: 'Neutral', value: 100000001 },
            'negative': { label: 'Negative', value: 100000002 }
        };

        const sentimentInfo = categoryMap[sentimentResult.sentiment] || categoryMap['neutral'];
        const confidenceScore = sentimentResult.confidenceScores[sentimentResult.sentiment] || 0.5;

        // Set priority based on sentiment - negative feedback gets escalated
        let priority = 'Medium';
        let priorityValue = 100000001;

        if (sentimentResult.sentiment === 'negative' && confidenceScore > 0.7) {
            priority = 'Critical';
            priorityValue = 100000003;
        } else if (sentimentResult.sentiment === 'negative') {
            priority = 'High';
            priorityValue = 100000002;
        } else if (sentimentResult.sentiment === 'positive' && confidenceScore > 0.8) {
            priority = 'Low';
            priorityValue = 100000000;
        }

        enrichedFeedback.sentimentScore = parseFloat(confidenceScore.toFixed(2));
        enrichedFeedback.sentimentCategory = sentimentInfo.label;
        enrichedFeedback.sentimentCategoryValue = sentimentInfo.value;
        enrichedFeedback.priority = priority;
        enrichedFeedback.priorityValue = priorityValue;
        enrichedFeedback.keyPhrases = sentimentResult.keyPhrases.join(', ');
        enrichedFeedback.aiConfidence = sentimentResult.confidenceScores;

        processingLog.stages.push({
            stage: 3,
            name: 'Sentiment Analysis',
            status: 'Success',
            result: {
                sentiment: sentimentInfo.label,
                confidence: confidenceScore,
                priority: priority
            },
            timestamp: new Date().toISOString()
        });

        context.log('Sentiment:', sentimentInfo.label, '| Confidence:', confidenceScore);
        context.log('Priority:', priority);

        // Stage 4 - pull out entities (products, people, locations mentioned)
        context.log('--- Stage 4: Entity Extraction ---');
        const entityResult = await extractEntities(textToAnalyze, context);

        enrichedFeedback.entities = JSON.stringify(entityResult.entities);
        enrichedFeedback.entitySummary = entityResult.summary;

        processingLog.stages.push({
            stage: 4,
            name: 'Entity Extraction',
            status: 'Success',
            result: entityResult,
            timestamp: new Date().toISOString()
        });

        context.log('Entities found:', entityResult.summary);

        // Stage 5 - generate a draft response using GPT
        context.log('--- Stage 5: Auto-Response Generation ---');
        const autoResponse = await generateAutoResponse(
            feedback.customerName,
            textToAnalyze,
            sentimentInfo.label,
            entityResult.entities,
            context
        );

        enrichedFeedback.autoResponse = autoResponse;

        processingLog.stages.push({
            stage: 5,
            name: 'Auto-Response Generation',
            status: 'Success',
            result: { responseLength: autoResponse.length },
            timestamp: new Date().toISOString()
        });

        context.log('Auto-response generated, length:', autoResponse.length);

        // Wrap up
        processingLog.endTime = new Date().toISOString();
        processingLog.totalStages = 5;
        processingLog.successfulStages = processingLog.stages.filter(s => s.status === 'Success').length;

        enrichedFeedback.processingLog = JSON.stringify(processingLog);
        enrichedFeedback.processedAt = new Date().toISOString();

        context.log('=== PIPELINE COMPLETE ===');
        context.log('Processed feedback ID:', feedback.feedbackId || 'N/A', '| Sentiment:', enrichedFeedback.sentimentCategory, '| Priority:', enrichedFeedback.priority);

        // Push to topic for downstream processing (Logic App picks this up)
        await sendToServiceBusTopic(enrichedFeedback, context);

    } catch (error) {
        context.log.error('Pipeline error:', error);

        processingLog.stages.push({
            stage: 'Error',
            name: 'Pipeline Failure',
            status: 'Failed',
            error: error.message,
            timestamp: new Date().toISOString()
        });

        // If Azure AI fails, fall back to basic keyword matching
        const fallbackResult = basicSentiment(feedback.feedbackText);

        enrichedFeedback = {
            ...feedback,
            sentimentScore: fallbackResult.score,
            sentimentCategory: fallbackResult.category,
            sentimentCategoryValue: fallbackResult.value,
            priority: 'Medium',
            priorityValue: 100000001,
            processingLog: JSON.stringify(processingLog),
            processedAt: new Date().toISOString(),
            pipelineError: error.message
        };

        await sendToServiceBusTopic(enrichedFeedback, context);
    }
};

// Language detection using Azure Text Analytics
async function detectLanguage(text, context) {
    const endpoint = process.env.COGNITIVE_ENDPOINT;
    const apiKey = process.env.COGNITIVE_KEY;

    const url = `${endpoint}/text/analytics/v3.1/languages`;

    const requestBody = JSON.stringify({
        documents: [{ id: '1', text: text }]
    });

    const result = await makeCognitiveRequest(url, apiKey, requestBody, context);
    const doc = result.documents[0];
    const primaryLanguage = doc.detectedLanguage;

    return {
        language: primaryLanguage.iso6391Name,
        languageName: primaryLanguage.name,
        confidence: primaryLanguage.confidenceScore
    };
}

// Translation via Azure Translator
async function translateText(text, fromLanguage, context) {
    const apiKey = process.env.TRANSLATOR_KEY || process.env.COGNITIVE_KEY;
    const region = process.env.TRANSLATOR_REGION || 'eastus';

    const translatorEndpoint = 'https://api.cognitive.microsofttranslator.com';
    const url = `${translatorEndpoint}/translate?api-version=3.0&from=${fromLanguage}&to=en`;

    const requestBody = JSON.stringify([{ text: text }]);

    try {
        const result = await makeTranslatorRequest(url, apiKey, requestBody, region, context);
        return {
            translatedText: result[0].translations[0].text,
            from: fromLanguage,
            to: 'en'
        };
    } catch (error) {
        context.log.warn('Translation failed, using original text:', error.message);
        return {
            translatedText: text,
            from: fromLanguage,
            to: fromLanguage,
            error: error.message
        };
    }
}

// Sentiment + key phrase extraction
async function analyzeSentiment(text, context) {
    const endpoint = process.env.COGNITIVE_ENDPOINT;
    const apiKey = process.env.COGNITIVE_KEY;

    const requestBody = JSON.stringify({
        documents: [{ id: '1', language: 'en', text: text }]
    });

    const sentimentUrl = `${endpoint}/text/analytics/v3.1/sentiment`;
    const sentimentResult = await makeCognitiveRequest(sentimentUrl, apiKey, requestBody, context);

    const keyPhrasesUrl = `${endpoint}/text/analytics/v3.1/keyPhrases`;
    const keyPhrasesResult = await makeCognitiveRequest(keyPhrasesUrl, apiKey, requestBody, context);

    const doc = sentimentResult.documents[0];
    const keyPhrases = keyPhrasesResult.documents[0]?.keyPhrases || [];

    return {
        sentiment: doc.sentiment,
        confidenceScores: doc.confidenceScores,
        keyPhrases: keyPhrases
    };
}

// Named entity recognition - finds products, people, orgs, locations
async function extractEntities(text, context) {
    const endpoint = process.env.COGNITIVE_ENDPOINT;
    const apiKey = process.env.COGNITIVE_KEY;

    const url = `${endpoint}/text/analytics/v3.1/entities/recognition/general`;

    const requestBody = JSON.stringify({
        documents: [{ id: '1', language: 'en', text: text }]
    });

    const result = await makeCognitiveRequest(url, apiKey, requestBody, context);
    const doc = result.documents[0];

    // Group by category for easier reading
    const groupedEntities = {};
    doc.entities.forEach(entity => {
        const category = entity.category;
        if (!groupedEntities[category]) {
            groupedEntities[category] = [];
        }
        groupedEntities[category].push({
            text: entity.text,
            confidence: entity.confidenceScore,
            subcategory: entity.subcategory || null
        });
    });

    const summaryParts = [];
    if (groupedEntities.Product) summaryParts.push(`Products: ${groupedEntities.Product.map(e => e.text).join(', ')}`);
    if (groupedEntities.Person) summaryParts.push(`People: ${groupedEntities.Person.map(e => e.text).join(', ')}`);
    if (groupedEntities.Location) summaryParts.push(`Locations: ${groupedEntities.Location.map(e => e.text).join(', ')}`);
    if (groupedEntities.Organization) summaryParts.push(`Organizations: ${groupedEntities.Organization.map(e => e.text).join(', ')}`);

    return {
        entities: groupedEntities,
        summary: summaryParts.join(' | ') || 'No entities detected',
        totalCount: doc.entities.length
    };
}

// Draft response using Azure OpenAI
async function generateAutoResponse(customerName, feedbackText, sentiment, entities, context) {
    const endpoint = process.env.OPENAI_ENDPOINT;
    const apiKey = process.env.OPENAI_KEY;
    const deployment = process.env.OPENAI_DEPLOYMENT || 'gpt-35-turbo';

    if (!endpoint || !apiKey) {
        context.log.warn('OpenAI not configured, using template response');
        return generateTemplateResponse(customerName, sentiment);
    }

    const url = `${endpoint}/openai/deployments/${deployment}/chat/completions?api-version=2024-02-15-preview`;

    const systemPrompt = `You are a helpful customer service representative. Generate a professional, empathetic response to customer feedback.

Guidelines:
- Be warm and professional
- Acknowledge their specific concerns
- If sentiment is negative, apologize and offer to help
- If sentiment is positive, thank them genuinely
- Keep response under 150 words
- Do not make promises you cannot keep
- Sign off as "Customer Support Team"`;

    const userPrompt = `Customer Name: ${customerName}
Sentiment: ${sentiment}
Their Feedback: "${feedbackText}"
Entities Mentioned: ${JSON.stringify(entities)}

Generate an appropriate response:`;

    const requestBody = JSON.stringify({
        messages: [
            { role: 'system', content: systemPrompt },
            { role: 'user', content: userPrompt }
        ],
        max_tokens: 300,
        temperature: 0.7
    });

    try {
        const result = await makeOpenAIRequest(url, apiKey, requestBody, context);
        return result.choices[0].message.content;
    } catch (error) {
        context.log.warn('OpenAI failed, using template response:', error.message);
        return generateTemplateResponse(customerName, sentiment);
    }
}

// Fallback templates when OpenAI isn't available
function generateTemplateResponse(customerName, sentiment) {
    const templates = {
        'Positive': `Dear ${customerName},

Thank you so much for your wonderful feedback! We're thrilled to hear about your positive experience. Your kind words mean a lot to our team and motivate us to continue delivering excellent service.

We truly appreciate you taking the time to share your thoughts with us.

Best regards,
Customer Support Team`,

        'Negative': `Dear ${customerName},

Thank you for bringing this to our attention. We sincerely apologize for the experience you've had, and we understand your frustration.

Your feedback is invaluable in helping us improve. A member of our team will review your concerns and reach out to you within 24 hours to resolve this matter.

We appreciate your patience and the opportunity to make things right.

Best regards,
Customer Support Team`,

        'Neutral': `Dear ${customerName},

Thank you for taking the time to share your feedback with us. We value your input as it helps us understand how we can better serve you.

If you have any additional comments or questions, please don't hesitate to reach out.

Best regards,
Customer Support Team`
    };

    return templates[sentiment] || templates['Neutral'];
}

// HTTP helpers for Azure Cognitive Services
async function makeCognitiveRequest(url, apiKey, body, context) {
    return new Promise((resolve, reject) => {
        const urlObj = new URL(url);

        const options = {
            hostname: urlObj.hostname,
            path: urlObj.pathname + urlObj.search,
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Ocp-Apim-Subscription-Key': apiKey
            }
        };

        const req = https.request(options, (res) => {
            let data = '';
            res.on('data', (chunk) => { data += chunk; });
            res.on('end', () => {
                if (res.statusCode >= 200 && res.statusCode < 300) {
                    resolve(JSON.parse(data));
                } else {
                    reject(new Error(`Cognitive API returned ${res.statusCode}: ${data}`));
                }
            });
        });

        req.on('error', reject);
        req.write(body);
        req.end();
    });
}

async function makeTranslatorRequest(url, apiKey, body, region, context) {
    return new Promise((resolve, reject) => {
        const urlObj = new URL(url);

        const options = {
            hostname: urlObj.hostname,
            path: urlObj.pathname + urlObj.search,
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Ocp-Apim-Subscription-Key': apiKey,
                'Ocp-Apim-Subscription-Region': region
            }
        };

        const req = https.request(options, (res) => {
            let data = '';
            res.on('data', (chunk) => { data += chunk; });
            res.on('end', () => {
                if (res.statusCode >= 200 && res.statusCode < 300) {
                    resolve(JSON.parse(data));
                } else {
                    reject(new Error(`Translator API returned ${res.statusCode}: ${data}`));
                }
            });
        });

        req.on('error', reject);
        req.write(body);
        req.end();
    });
}

async function makeOpenAIRequest(url, apiKey, body, context) {
    return new Promise((resolve, reject) => {
        const urlObj = new URL(url);

        const options = {
            hostname: urlObj.hostname,
            path: urlObj.pathname + urlObj.search,
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'api-key': apiKey
            }
        };

        const req = https.request(options, (res) => {
            let data = '';
            res.on('data', (chunk) => { data += chunk; });
            res.on('end', () => {
                if (res.statusCode >= 200 && res.statusCode < 300) {
                    resolve(JSON.parse(data));
                } else {
                    reject(new Error(`OpenAI API returned ${res.statusCode}: ${data}`));
                }
            });
        });

        req.on('error', reject);
        req.write(body);
        req.end();
    });
}

// Send processed feedback to Service Bus topic for Logic App to pick up
async function sendToServiceBusTopic(enrichedFeedback, context) {
    const namespace = 'sb-feedback-demo';
    const topicName = 'feedback-analyzed';
    const keyName = 'RootManageSharedAccessKey';
    const key = getServiceBusKey();

    const sasToken = createSasToken(`https://${namespace}.servicebus.windows.net/${topicName}`, keyName, key);
    const messageBody = JSON.stringify(enrichedFeedback);

    const options = {
        hostname: `${namespace}.servicebus.windows.net`,
        path: `/${topicName}/messages`,
        method: 'POST',
        headers: {
            'Authorization': sasToken,
            'Content-Type': 'application/json',
            'Content-Length': Buffer.byteLength(messageBody),
            'BrokerProperties': JSON.stringify({ 'Label': enrichedFeedback.sentimentCategory }),
            'sentimentCategory': enrichedFeedback.sentimentCategory,
            'priority': enrichedFeedback.priority
        }
    };

    return new Promise((resolve, reject) => {
        const req = https.request(options, (res) => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                if (res.statusCode === 201 || res.statusCode === 200) {
                    context.log('Message sent successfully to topic');
                    resolve();
                } else {
                    reject(new Error(`Failed to send: ${res.statusCode} ${data}`));
                }
            });
        });

        req.on('error', reject);
        req.write(messageBody);
        req.end();
    });
}

function getServiceBusKey() {
    const connectionString = process.env['sb-feedback-demo_RootManageSharedAccessKey_SERVICEBUS'];
    const keyMatch = connectionString.match(/SharedAccessKey=([^;]+)/);
    return keyMatch ? keyMatch[1] : '';
}

function createSasToken(uri, keyName, key) {
    const encoded = encodeURIComponent(uri);
    const ttl = Math.round(Date.now() / 1000) + 3600;
    const signature = encoded + '\n' + ttl;
    const signatureBytes = crypto.createHmac('sha256', key).update(signature).digest('base64');
    return `SharedAccessSignature sr=${encoded}&sig=${encodeURIComponent(signatureBytes)}&se=${ttl}&skn=${keyName}`;
}

// Simple keyword-based sentiment when Azure AI isn't available
function basicSentiment(text) {
    const lowerText = text.toLowerCase();
    const positiveWords = ['great', 'excellent', 'amazing', 'love', 'fantastic', 'wonderful', 'awesome', 'super', 'useful', 'helpful'];
    const negativeWords = ['bad', 'terrible', 'awful', 'hate', 'worst', 'poor', 'disappointed', 'frustrated', 'angry', 'useless'];

    let positiveCount = positiveWords.filter(word => lowerText.includes(word)).length;
    let negativeCount = negativeWords.filter(word => lowerText.includes(word)).length;

    if (positiveCount > negativeCount) {
        return { category: 'Positive', value: 100000000, score: 0.75 };
    } else if (negativeCount > positiveCount) {
        return { category: 'Negative', value: 100000002, score: 0.25 };
    }
    return { category: 'Neutral', value: 100000001, score: 0.50 };
}
