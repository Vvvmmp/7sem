package main

import (
	"context"
	"flag"
	"fmt"
	"log"
	"time"

	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/status"

	pb "grpcclient02/calc"
)

func main() {
	addr := flag.String("addr", "localhost:5002", "gRPC server address")
	flag.Parse()
	fmt.Printf("gRPCClient02 (Go) -> %s\n\n", *addr)

	conn, err := grpc.NewClient(*addr, grpc.WithTransportCredentials(insecure.NewCredentials()))
	if err != nil {
		log.Fatalf("connect: %v", err)
	}
	defer conn.Close()
	client := pb.NewCalculatorClient(conn)

	call := func(name string, rpc func(ctx context.Context) (any, error)) {
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		res, err := rpc(ctx)
		if err != nil {
			st, _ := status.FromError(err)
			fmt.Printf("%-16s -> ERROR %s: %s\n", name, st.Code(), st.Message())
			return
		}
		fmt.Printf("%-16s = %v\n", name, res)
	}

	req := func(x, y float64) *pb.BinaryRequest { return &pb.BinaryRequest{X: x, Y: y} }
	double := func(r *pb.DoubleReply, err error) (any, error) {
		if err != nil {
			return nil, err
		}
		return r.GetResult(), nil
	}
	integer := func(r *pb.IntReply, err error) (any, error) {
		if err != nil {
			return nil, err
		}
		return r.GetResult(), nil
	}

	call("SUM(7.5, 2.5)", func(ctx context.Context) (any, error) { return double(client.Sum(ctx, req(7.5, 2.5))) })
	call("SUB(7.5, 2.5)", func(ctx context.Context) (any, error) { return double(client.Sub(ctx, req(7.5, 2.5))) })
	call("MUL(7.5, 2.5)", func(ctx context.Context) (any, error) { return double(client.Mul(ctx, req(7.5, 2.5))) })
	call("DIV(7.5, 2.5)", func(ctx context.Context) (any, error) { return double(client.Div(ctx, req(7.5, 2.5))) })
	call("DIV(7.5, 0)", func(ctx context.Context) (any, error) { return double(client.Div(ctx, req(7.5, 0))) })
	for _, x := range []int32{5, 12, 13, -1} {
		call(fmt.Sprintf("FACT(%d)", x), func(ctx context.Context) (any, error) {
			return integer(client.Fact(ctx, &pb.FactRequest{X: x}))
		})
	}
}
