// Voice Live WebSocket client for real-time voice chat with Azure AI Foundry Agent
let websocket = null;
let audioContext = null;
let audioWorkletNode = null;
let mediaStream = null;
let dotNetRef = null;
let isConnected = false;
let audioQueue = [];
let isPlayingAudio = false;
let currentAudioSource = null;
let nextPlayTime = 0;

export async function initialize(websocketUrl, apiVersion, projectName, agentId, agentAccessToken, authorizationToken, speechKey, dotNetReference) {
    try {
        dotNetRef = dotNetReference;
        
        const baseUrl = `wss://${projectName}.cognitiveservices.azure.com/voice-agent/realtime`;
        const clientRequestId = crypto.randomUUID();
         
        const params = new URLSearchParams({
            'api-version': apiVersion,
            'x-ms-client-request-id': clientRequestId,
            'agent_id': agentId,
            'agent-project-name': projectName,
            'agent_access_token': agentAccessToken,
            'Authorization': `Bearer ${authorizationToken}`
        });
        
        const url = `${baseUrl}?${params.toString()}`;
        
        websocket = new WebSocket(url);
        
        websocket.onopen = () => {
            console.log('Voice Live connected');
            isConnected = true;
            
            // Configure session settings
            sendMessage({
                type: 'session.update',
                session: {
                    turn_detection: {
                        type: 'azure_semantic_vad',
                        threshold: 0.3,
                        prefix_padding_ms: 200,
                        silence_duration_ms: 200,
                        remove_filler_words: false,
                        end_of_utterance_detection: {
                            model: 'semantic_detection_v1',
                            threshold_level: 'default',
                            timeout_ms: 1000
                        }
                    },
                    input_audio_noise_reduction: {
                        type: 'azure_deep_noise_suppression'
                    },
                    input_audio_echo_cancellation: {
                        type: 'server_echo_cancellation'
                    },
                    voice: {
                        name: 'en-US-Ava:DragonHDLatestNeural',
                        type: 'azure-standard',
                        temperature: 0.8
                    }
                }
            });
            
            dotNetRef.invokeMethodAsync('OnConnectionOpened');
        };
        
        websocket.onmessage = async (event) => {
            try {
                const message = JSON.parse(event.data);
                await handleWebSocketMessage(message);
            } catch (error) {
                console.error('Error handling message:', error);
            }
        };
        
        websocket.onerror = (error) => {
            console.error('WebSocket error:', error);
            dotNetRef.invokeMethodAsync('OnError', 'WebSocket connection error. Please check your configuration.');
        };
        
        websocket.onclose = (event) => {
            isConnected = false;
            cleanup();
            
            let closeReason = 'Connection closed';
            if (event.code === 1008) {
                closeReason = 'Authentication failed. Please verify your credentials.';
            } else if (event.code === 1006) {
                closeReason = 'Connection abnormally closed. Please check your network connection.';
            } else if (event.reason) {
                closeReason = event.reason;
            }
            
            console.log(`Voice Live disconnected: ${closeReason} (code: ${event.code})`);
            dotNetRef.invokeMethodAsync('OnConnectionClosed');
            
            if (event.code !== 1000) {
                dotNetRef.invokeMethodAsync('OnError', closeReason);
            }
        };
        
        audioContext = new (window.AudioContext || window.webkitAudioContext)({
            sampleRate: 24000
        });
        
        // Initialize nextPlayTime
        nextPlayTime = audioContext.currentTime;
        
    } catch (error) {
        console.error('Error initializing Voice Live:', error);
        dotNetRef.invokeMethodAsync('OnError', error.message);
    }
}

async function handleWebSocketMessage(message) {
    switch (message.type) {
        case 'session.created':
        case 'session.updated':
            console.log(`Session ${message.type.split('.')[1]}`);
            break;
            
        case 'conversation.item.input_audio_transcription.completed':
            if (message.transcript) {
                dotNetRef.invokeMethodAsync('OnUserTranscript', message.transcript);
            }
            break;
            
        case 'response.audio_transcript.done':
            if (message.transcript) {
                dotNetRef.invokeMethodAsync('OnAgentResponse', message.transcript);
            }
            break;
            
        case 'response.audio.delta':
            if (message.delta) {
                // Play audio chunks immediately as they arrive for lower latency
                await playAudioChunkStreaming(message.delta);
            }
            break;
            
        case 'response.audio.done':
            // Audio streaming complete
            break;
            
        case 'response.done':
            // Reset play time for next response
            nextPlayTime = audioContext.currentTime;
            break;
            
        case 'error':
            console.error('Voice Live error:', message.error?.message || 'Unknown error');
            dotNetRef.invokeMethodAsync('OnError', message.error?.message || 'Unknown error occurred');
            break;
            
        default:
            // Log unhandled message types for debugging
            console.log('Unhandled message type:', message.type);
    }
}

async function playAudioChunkStreaming(base64Audio) {
    try {
        // Decode the base64 audio chunk
        const binaryString = atob(base64Audio);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }
        
        // Convert to audio buffer
        const int16Array = new Int16Array(bytes.buffer);
        const float32Array = new Float32Array(int16Array.length);
        for (let i = 0; i < int16Array.length; i++) {
            float32Array[i] = int16Array[i] / 32768.0;
        }
        
        const audioBuffer = audioContext.createBuffer(1, float32Array.length, 24000);
        audioBuffer.getChannelData(0).set(float32Array);
        
        // Schedule this chunk to play immediately after the previous one
        const source = audioContext.createBufferSource();
        source.buffer = audioBuffer;
        source.connect(audioContext.destination);
        
        // Use current time if we're ahead of schedule, otherwise use nextPlayTime
        const startTime = Math.max(audioContext.currentTime, nextPlayTime);
        source.start(startTime);
        
        // Update nextPlayTime for the next chunk
        nextPlayTime = startTime + audioBuffer.duration;
        
    } catch (error) {
        console.error('Error playing audio chunk:', error);
    }
}

export async function startListening() {
    try {
        if (!isConnected) {
            throw new Error('Not connected to Voice Live');
        }
        
        // Stop any playing audio before starting to listen
        nextPlayTime = audioContext.currentTime;
        
        mediaStream = await navigator.mediaDevices.getUserMedia({
            audio: {
                channelCount: 1,
                sampleRate: 24000,
                echoCancellation: true,
                noiseSuppression: true
            }
        });
        
        const source = audioContext.createMediaStreamSource(mediaStream);
        const bufferSize = 4096;
        const processor = audioContext.createScriptProcessor(bufferSize, 1, 1);
        
        processor.onaudioprocess = (event) => {
            const inputData = event.inputBuffer.getChannelData(0);
            const pcm16 = new Int16Array(inputData.length);
            for (let i = 0; i < inputData.length; i++) {
                const s = Math.max(-1, Math.min(1, inputData[i]));
                pcm16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
            }
            
            const base64Audio = arrayBufferToBase64(pcm16.buffer);
            
            if (websocket && websocket.readyState === WebSocket.OPEN) {
                sendMessage({
                    type: 'input_audio_buffer.append',
                    audio: base64Audio
                });
            }
        };
        
        source.connect(processor);
        processor.connect(audioContext.destination);
        audioWorkletNode = processor;
        
    } catch (error) {
        console.error('Error starting microphone:', error);
        dotNetRef.invokeMethodAsync('OnError', 'Failed to access microphone: ' + error.message);
    }
}

export function stopListening() {
    if (audioWorkletNode) {
        audioWorkletNode.disconnect();
        audioWorkletNode = null;
    }
    
    if (mediaStream) {
        mediaStream.getTracks().forEach(track => track.stop());
        mediaStream = null;
    }
}

export function sendAudio() {
    if (!isConnected) {
        console.error('Not connected to Voice Live');
        return;
    }
    
    stopListening();
    
    sendMessage({
        type: 'input_audio_buffer.commit'
    });
    
    sendMessage({
        type: 'response.create',
        response: {
            modalities: ['text', 'audio'],
            instructions: 'Please respond to the user\'s question.'
        }
    });
}

export function disconnect() {
    stopListening();
    
    // Stop any playing audio
    if (currentAudioSource) {
        currentAudioSource.stop();
        currentAudioSource = null;
    }
    
    if (websocket) {
        websocket.close();
        websocket = null;
    }
    
    cleanup();
}

function cleanup() {
    stopListening();
    
    if (audioContext) {
        audioContext.close();
        audioContext = null;
    }
    
    audioQueue = [];
    isPlayingAudio = false;
    currentAudioSource = null;
    nextPlayTime = 0;
    isConnected = false;
}

function sendMessage(message) {
    if (websocket && websocket.readyState === WebSocket.OPEN) {
        websocket.send(JSON.stringify(message));
    } else {
        console.error('WebSocket not connected');
    }
}

function arrayBufferToBase64(buffer) {
    let binary = '';
    const bytes = new Uint8Array(buffer);
    const len = bytes.byteLength;
    for (let i = 0; i < len; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary);
}
