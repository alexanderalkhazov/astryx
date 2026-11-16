// Sets Development-only prototype faults: FAULTS is a JSON body for POST /dev/faults,
// e.g. {"marketData":"down"}, {"scanner":"failing"}, {"prices":{"NVDA":"177.00"}}.
const response = http.post(`${API_URL}/dev/faults`, { headers: { 'Content-Type': 'application/json' }, body: FAULTS });
if (response.status !== 200) throw new Error(`dev faults ${FAULTS}: HTTP ${response.status}`);
