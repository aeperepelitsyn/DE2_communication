--------------------------------------------------------------------------------
-- Module: de2_communication
-- Author: Artem Perepelitson
-- Date  : 2018.01.10 22:55
-- Brief : VHDL part of interface for connection Altera DE2 board to computer
--------------------------------------------------------------------------------
library ieee;
use ieee.std_logic_1164.all;
use ieee.numeric_std.all;
--------------------------------------------------------------------------------
entity de2_communication is
	port(	
			nrst: in std_logic; -- Not Reset
			clk : in std_logic; -- 50MHz
			
			request7bit : out std_logic_vector( 6 downto 0);
			requesting  : out std_logic; -- '1' means that 'request7bit' is received
			writeenable : in  std_logic; -- captured at falling edge of 'requesting'
			wtag7bit    : in  std_logic_vector( 6 downto 0);
			wdata8bit   : in  std_logic_vector( 7 downto 0);
			wdata16bit  : in  std_logic_vector(15 downto 0);
			wdata32bit  : in  std_logic_vector(31 downto 0);
			
			rtag7bit    : out std_logic_vector( 6 downto 0);
			rdata8bit   : out std_logic_vector( 7 downto 0);
			rdata16bit  : out std_logic_vector(15 downto 0);
			rdata32bit  : out std_logic_vector(31 downto 0);
			received    : out std_logic; -- '1' means that tag and data are received
			--	JTAG
			TDO: out std_logic; -- PIN_F14
			TDI: in  std_logic; -- PIN_B14
			TCS: in  std_logic; -- PIN_A14
			TCK: in  std_logic  -- PIN_D14
	);
end entity de2_communication;
--------------------------------------------------------------------------------
architecture rtl of de2_communication is
	component USB_JTAG is
		port(
			--	HOST
			iTxD_DATA   : in  std_logic_vector(7 downto 0);
			oTxD_Done   : out std_logic;
			iTxD_Start  : in  std_logic;
			oRxD_DATA   : out std_logic_vector(7 downto 0);
			oRxD_Ready  : out std_logic;
			iRST_n,iCLK : in  std_logic;
			--	JTAG
			TDO: out std_logic;
			TDI: in  std_logic;
			TCS: in  std_logic;
			TCK: in  std_logic
		);
	end component USB_JTAG;
	
	signal iTxD_DATA  : std_logic_vector(7 downto 0);
	signal oTxD_Done  : std_logic;
	signal iTxD_Start : std_logic;
	signal oRxD_DATA  : std_logic_vector(7 downto 0);
	signal oRxD_Ready : std_logic;
	
	signal zeroth : std_logic:= '0';
	
	type bytes is array(natural range <>) of std_logic_vector(7 downto 0);
	signal rxdata, txdata, data, tdata : bytes (0 to 7);
	signal rindex: natural range 0 to 7 := 0;
	
	constant skipping : natural := 1535; -- empiric non parameterized constant
	
	 -- Enumerated type for FSM (Finite-State Machine)
	type state_type is (idle, delay, receiving, extra, waiting, returning);
	signal rstate, wstate : state_type;        -- Register to hold FSMs state
begin
	jtag: USB_JTAG port map (
		iTxD_DATA   => iTxD_DATA ,
		oTxD_Done   => oTxD_Done ,
		iTxD_Start  => iTxD_Start,
		oRxD_DATA   => oRxD_DATA,
		oRxD_Ready  => oRxD_Ready,
		
		iRST_n => nrst,
		iCLK   => clk,
		--	JTAG
		TDO => TDO,
		TDI => TDI,
		TCS => TCS,
		TCK => TCK
	);
	
	txdata(0) <= "1" & wtag7bit;
	txdata(1) <= wdata8bit;
	txdata(2) <= wdata16bit( 7 downto 0 );
	txdata(3) <= wdata16bit(15 downto 8 );
	txdata(4) <= wdata32bit( 7 downto 0 );
	txdata(5) <= wdata32bit(15 downto 8 );
	txdata(6) <= wdata32bit(23 downto 16);
	txdata(7) <= wdata32bit(31 downto 24);
	
	rtag7bit   <= rxdata(0)( 6 downto 0 );
	rdata8bit  <= rxdata(1);
	rdata16bit <= rxdata(3) & rxdata(2);
	rdata32bit <= rxdata(7) & rxdata(6) & rxdata(5) & rxdata(4);
	data(rindex)<= oRxD_DATA;
	zeroth <= '1' when rindex = 0 else '0';
	
	-- Receiving FSM 
	rx: process (clk, nrst)
		variable counter: natural range 0 to 3 := 0;
	begin
		if nrst = '0' then
			rstate <= idle;
		elsif (rising_edge(clk)) then
			case rstate is
				when idle=>
					received <= '0';
					requesting <= '0';
					if oRxD_Ready = '1' then
						if oRxD_DATA(7) = '1' then
							rstate <= delay;
							request7bit <= oRxD_DATA(6 downto 0);
						else
							rstate <= extra;
						end if;
					else
						rstate <= idle;
					end if;
				when delay=>
					if counter < 3 then
						counter := counter + 1;
						requesting <= '1';
					else
						if writeenable = '1' then
							rstate <= receiving;
							tdata <= txdata;
						else
							rstate <= returning;
						end if;
						counter := 0;
						requesting <= '0';
					end if;
				when receiving=>
					if wstate = receiving then 
						rstate <= returning;
					else
						rstate <= receiving;
					end if;
				when extra=>
					rstate <= waiting;
					if rindex < 7 then 
						rindex <= rindex + 1;
					else
						rindex <= 0;
						rxdata <= data;
					end if;
				when waiting=>
					received <= zeroth;
					rstate <= returning;
				when returning =>
					if oRxD_Ready = '0' then
						rstate <= idle;
					else
						rstate <= returning;
					end if;
			end case;
		end if;
	end process;
	
	-- Transmitting FSM 
	tx: process (clk, nrst)
		variable counter: natural range 0 to skipping := 0;
		variable windex : natural range 0 to 7 := 0;
	begin
		if nrst = '0' then
			wstate <= idle;
			windex := 0;
		elsif (rising_edge(clk)) then
			case wstate is
				when idle=>
					if oTxD_Done = '0' then
						if (rstate = receiving) then
							iTxD_DATA <= tdata(windex);
							if (windex < 7) then
								windex := windex + 1;
								wstate <= delay;
							else
								windex := 0;
								wstate <= receiving;
							end if;
						else
							iTxD_DATA <= (Others => '0');
							wstate <= delay;
						end if;
					else
						wstate <= idle;
					end if;
				when receiving=>
					windex := 0;
					if rstate = receiving then
						wstate <= receiving;
					else
						wstate <= delay;
					end if;
				when delay=>
					if counter < skipping then
						counter := counter + 1;
					else
						counter := 0;
						iTxD_Start <= '1';
						wstate <= extra;
					end if;
				when extra=>
					wstate <= waiting;
				when waiting =>
					if oTxD_Done = '1' then
						iTxD_Start <= '0';
						wstate <= returning;
					else
						wstate <= waiting;
					end if;
				when returning =>
					if counter < 15 then
						counter := counter + 1;
					else
						counter := 0;
						wstate <= idle;
					end if;
			end case;
		end if;
	end process;
	
end;
--------------------------------------------------------------------------------
