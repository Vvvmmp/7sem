package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
)

type Request struct {
	JSONRPC string `json:"jsonrpc"`
	Method  string `json:"method"`
	Params  any    `json:"params,omitempty"`
	ID      *int   `json:"id,omitempty"`
}

type RPCError struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
	Data    any    `json:"data,omitempty"`
}

type Response struct {
	JSONRPC string          `json:"jsonrpc"`
	Result  json.RawMessage `json:"result,omitempty"`
	Error   *RPCError       `json:"error,omitempty"`
	ID      any             `json:"id"`
}

type Client struct {
	URL    string
	nextID int
}

func (c *Client) newRequest(method string, params any) Request {
	c.nextID++
	id := c.nextID
	return Request{JSONRPC: "2.0", Method: method, Params: params, ID: &id}
}

func (c *Client) post(body any) ([]byte, int, error) {
	data, err := json.Marshal(body)
	if err != nil {
		return nil, 0, err
	}
	fmt.Printf("--> %s\n", data)

	resp, err := http.Post(c.URL, "application/json", bytes.NewReader(data))
	if err != nil {
		return nil, 0, err
	}
	defer resp.Body.Close()
	raw, err := io.ReadAll(resp.Body)
	if len(raw) > 0 {
		fmt.Printf("<-- %s\n", raw)
	} else {
		fmt.Printf("<-- HTTP %d (пустое тело)\n", resp.StatusCode)
	}
	return raw, resp.StatusCode, err
}

func (c *Client) Call(method string, params any) (*Response, error) {
	raw, _, err := c.post(c.newRequest(method, params))
	if err != nil {
		return nil, err
	}
	var r Response
	return &r, json.Unmarshal(raw, &r)
}

func (c *Client) Notify(method string, params any) error {
	_, _, err := c.post(Request{JSONRPC: "2.0", Method: method, Params: params})
	return err
}

func (c *Client) Batch(reqs []Request) ([]Response, error) {
	raw, _, err := c.post(reqs)
	if err != nil || len(raw) == 0 {
		return nil, err
	}
	var rs []Response
	return rs, json.Unmarshal(raw, &rs)
}

func printResult(r *Response) {
	if r.Error != nil {
		fmt.Printf("    id=%v ОШИБКА %d: %s", r.ID, r.Error.Code, r.Error.Message)
		if r.Error.Data != nil {
			fmt.Printf(" (%v)", r.Error.Data)
		}
		fmt.Println()
	} else {
		fmt.Printf("    id=%v РЕЗУЛЬТАТ: %s\n", r.ID, r.Result)
	}
}

func section(title string) { fmt.Printf("\n=== %s ===\n", title) }

func main() {
	url := "http://localhost:5000/jsonrpc"
	if len(os.Args) > 1 {
		url = os.Args[1]
	}
	c := &Client{URL: url}

	type named = map[string]any

	calls := []struct {
		title  string
		method string
		params any
	}{
		{"SUM позиционно (3 + 4)", "SUM", []any{3, 4}},
		{"SUM по именам (x=1.5, y=2.25)", "SUM", named{"x": 1.5, "y": 2.25}},
		{"SUB позиционно (10 - 4.5)", "SUB", []any{10, 4.5}},
		{"SUB по именам (y=10, x=3) — порядок ключей не важен", "SUB", named{"y": 10, "x": 3}},
		{"MUL позиционно (6 * 7)", "MUL", []any{6, 7}},
		{"MUL по именам (x=-2, y=2.5)", "MUL", named{"x": -2, "y": 2.5}},
		{"DIV позиционно (10 / 4)", "DIV", []any{10, 4}},
		{"DIV по именам (x=1, y=3)", "DIV", named{"x": 1, "y": 3}},
		{"DIV на ноль -> ошибка", "DIV", []any{5, 0}},
		{"FACT позиционно (5!)", "FACT", []any{5}},
		{"FACT по именам (12!) — максимум для int", "FACT", named{"x": 12}},
		{"FACT 13! -> превышение int, ошибка", "FACT", []any{13}},
		{"Неизвестный метод -> ошибка -32601", "POW", []any{2, 3}},
		{"Неверное число параметров -> ошибка -32602", "SUM", []any{1}},
	}

	for _, call := range calls {
		section(call.title)
		r, err := c.Call(call.method, call.params)
		if err != nil {
			fmt.Println("ошибка транспорта:", err)
			os.Exit(1)
		}
		printResult(r)
	}

	section("Уведомление (без id) -> сервер не отвечает")
	if err := c.Notify("SUM", []any{1, 1}); err != nil {
		fmt.Println("ошибка транспорта:", err)
	}

	section("Пакетный вызов (batch): 5 запросов + 1 уведомление одним HTTP-запросом")
	batch := []Request{
		c.newRequest("SUM", []any{1, 2}),
		c.newRequest("MUL", named{"x": 3, "y": 4}),
		c.newRequest("DIV", []any{1, 0}),
		c.newRequest("FACT", named{"x": 10}),
		{JSONRPC: "2.0", Method: "SUB", Params: []any{9, 9}},
		c.newRequest("FACT", []any{20}),
	}
	rs, err := c.Batch(batch)
	if err != nil {
		fmt.Println("ошибка:", err)
		os.Exit(1)
	}
	for i := range rs {
		printResult(&rs[i])
	}
}
