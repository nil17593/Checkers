using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Board : MonoBehaviourPunCallbacks
{
    public static Board Instance;

    [Header("Instantiable Prefabs")]
    [SerializeField] private GameObject highlightPrefab;
    [SerializeField] private GameObject movePrefab;

    [Header("Game Manager")]
    private GameManager gm;
    private Vector2 mouseOver;
    private List<Move> highlights = new List<Move>(); //List of all possible moves

    [Header("Piece Attributes")]
    private bool isClickable = true;
    private Piece selected; //Selected piece (null if none selected)
    private Move sMove;
    private bool multiCapture;
    private Vector2 pieceOffset = new Vector2(0.5f, 0.5f);

    [Header("Player Attributes")]
    private const string Player1Color = "Dark";
    private const string Player2Color = "Light";

    public string player1Name;
    public string player2Name;

    private int priority;


    [Header("Board Attributes")]
    private PhotonView pv;
    private int turn = 1; //1 = player 1; 2 = player 2
    private readonly Vector2 boardOffset = new Vector2(-4.0f, -4.0f);
    public bool gameCompleted;

    [Header("GUI")]
    [SerializeField] private GameObject winGUI;
    [SerializeField] private GameObject restartBtn;
    [SerializeField] private GameObject playerSelected1;
    [SerializeField] private GameObject playerSelected2;
    [SerializeField] private TextMeshProUGUI winText;

    private int p1Actor;

    private void Awake()
    {
        Instance = this;
        pv = GetComponent<PhotonView>();
    }

    private void Start()
    {
        // Default conditions
        isClickable = true;
        gameCompleted = false;
        playerSelected1.SetActive(true);
        playerSelected2.SetActive(false);

        CreateBoard();

        if (!IsPlayer1())
        {
            Camera.main.transform.rotation = Quaternion.Euler(0, 0, 180);
        }
        //Set player1 and player2 color
        winGUI.SetActive(false);
        //Debug.Log(PhotonNetwork.LocalPlayer);
    }

    void Update()
    {
        // Enforces player to go back to lobby
        CheckPlayerNum();


            UpdateMouseOver();
            // For debugging wins
            DebugWin();
            // For clicking pieces
            ClickPiece();
       
    }

    //Returns true if player is the first player in the playerlist
    public bool IsPlayer1()
    {
        //Debug.Log(PhotonNetwork.LocalPlayer);
        return PhotonNetwork.PlayerList.GetValue(0).Equals(PhotonNetwork.LocalPlayer);
    }

    private void CheckPlayerNum()
    {
        if (PhotonNetwork.CurrentRoom != null)
        {
            // For awarding win to player if the other player disconnects
            if (PhotonNetwork.CurrentRoom.PlayerCount <= 1 && !gameCompleted)
            {
                pv.RPC("EndGame", RpcTarget.All, p1Actor == PhotonNetwork.LocalPlayer.ActorNumber ? 1 : 2);
                PhotonNetwork.CurrentRoom.IsVisible = false;
                restartBtn.SetActive(false);
            }
            else if (PhotonNetwork.CurrentRoom.PlayerCount <= 1 && gameCompleted)
            {
                PhotonNetwork.CurrentRoom.IsVisible = false;
                restartBtn.SetActive(false);
            }

            if (PhotonNetwork.CurrentRoom.PlayerCount == 2 && PhotonNetwork.IsMasterClient)
            {
                restartBtn.SetActive(true);
            }
        }

    }


    public void RestartGame()
    {
        PhotonNetwork.DestroyAll();
        pv.RPC("RpcRestart", RpcTarget.All);
    }

    [PunRPC]
    private void RpcRestart()
    {
        winGUI.SetActive(false);
        PhotonNetwork.LoadLevel(SceneManager.GetActiveScene().buildIndex);
    }


    private void ClickPiece()
    {
        if (!isClickable)
            return;

        var x = (int)mouseOver.x;
        var y = (int)mouseOver.y;

        if (!Input.GetMouseButtonDown(0)) return;

        Debug.Log("Clicked at: " + x + ", " + y);
        if ((IsPlayer1() ? 1 : 2) != turn)
            return;
        if (turn == 2)
        {
            x = 7 - x;
            y = 7 - y;
        }

        if (multiCapture)
        {
            var selectedMove = CheckValid(x, y);
            if (selectedMove != null)
            {
                sMove = selectedMove;
                MovePiece();

                if (!multiCapture)
                    Invoke(nameof(CheckWin), 0.1f);
            }
            if (!multiCapture)
            {
                ClearHighlights();
                //DebugBoard();
            }
        }
        else if (selected == null) //No pieces are selected
        {
            SelectPiece(x, y);
        }
        else //A piece is already selected
        {
            selected.Select(false);
            if (SelectPiece(x, y))//If not selecting another piece
                return;

            Move selectedMove = CheckValid(x, y);
            if (selectedMove != null)
            {
                sMove = selectedMove;
                MovePiece();

                //Wait 0.1 sec second and check if valid
                //might need more testing depending on ping
                if (!multiCapture)
                    Invoke(nameof(CheckWin), 0.1f);
            }
            if (!multiCapture)
            {
                ClearHighlights();
                //DebugBoard();
            }
        }
    }

    private Piece[,] GetActivePieces()
    {
        var pieces = new Piece[8, 8];
        Piece p;
        foreach (Transform child in transform.Find("Pieces"))
        {
            p = child.GetComponent<Piece>();
            pieces[p.GetX(), p.GetY()] = p;
        }

        return pieces;
    }

    private void MovePiece()
    {
        var p = selected;
        var move = sMove;

        var x = move.GetX();
        var y = move.GetY();
        //Debug.Log("Moved piece " + p.GetX() + " " + p.GetY() + " to " + x + " " + y);

        // NetSynced Move
        var pView = p.GetComponent<PhotonView>();
        pView.RPC("Move", RpcTarget.All, x, y);

        ClearHighlights();

        //Delete captured piece
        var capture = move.GetCapture();
        if (capture != null)
        {
            int cX = capture.GetX();
            int cY = capture.GetY();

            pView = capture.GetComponent<PhotonView>();
            pView.RPC("DestroyPiece", RpcTarget.All);

            //clear all moves
            ClearMoves();

            //find additional capture
            FindMultiCapture(x, y, x - cX, y - cY);
        }

        if (multiCapture)
        {
            selected.Select(true);
            DisplayMoves();
        }
        else
        {
            ClearMoves();

            selected.Select(false);
            selected = null;

            HighlightMovable();
            pv.RPC("ChangeTurn", RpcTarget.All);
        }

        //Promote the piece
        if ((p.GetPlayer() == 1 && y == 7) ||
            (p.GetPlayer() == 2 && y == 0))
            p.GetComponent<PhotonView>().RPC("Promote", RpcTarget.All);

    }

    private void ClearMoves()
    {
        var pieces = GetActivePieces();
        for (var i = 0; i < 8; i++)
        {
            for (var j = 0; j < 8; j++)
            {
                var tmpP = pieces[i, j];
                if (tmpP != null)
                    tmpP.ClearMoves();
            }
        }
    }

    [PunRPC]
    public void ChangeTurn()
    {
        if (!gameCompleted)
        {
            if (turn == 1)
            {
                turn = 2;
                playerSelected1.SetActive(false);
                playerSelected2.SetActive(true);
            }
            else
            {
                turn = 1;
                playerSelected1.SetActive(true);
                playerSelected2.SetActive(false);
            }


            //Debug.Log("Player " + turn + "'s turn / " + ((turn == 1) ? player1Color : player2Color));
            Invoke(nameof(FindMoves), 0.1f);
        }
    }

    //Check if the selected move is in the list of valid moves for the selected piece
    private Move CheckValid(int x, int y)
    {
        foreach (var h in highlights)
        {
            if (h.GetX() == x && h.GetY() == y)
                return h;
        }

        return null;
    }

    //Get mouse location
    private void UpdateMouseOver()
    {
        if (!Camera.main || gameCompleted)
        {
            //Debug.Log("Unable to find main camera");
            return;
        }

        if (!gameCompleted)
        {
            RaycastHit hit;
            if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, 25.0f, LayerMask.GetMask("Board")))
            {
                Vector3 mousePoint = hit.point;
                //Debug.Log("Mouse Point before rotation: " + mousePoint);
                // Apply the inverse rotation to the mouse point
                if (!IsPlayer1())
                {
                    mousePoint.x = -mousePoint.x;
                    mousePoint.y = -mousePoint.y;
                }

                //Debug.Log("Mouse Point after rotation: " + mousePoint);

                mouseOver.x = (int)(mousePoint.x - boardOffset.x);
                mouseOver.y = (int)(mousePoint.y - boardOffset.y);
            }
            else
            {
                mouseOver.x = -1;
                mouseOver.y = -1;
            }
        }
    }



    //Create all pieces
    // [PunRPC]
    public void CreateBoard()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            //Debug.Log("ONLY MASTER");
            p1Actor = PhotonNetwork.LocalPlayer.ActorNumber;

            for (var y = 0; y < 3; y++)
            {
                for (var x = 0; x < 8; x += 2)
                    CreatePiece(x + y % 2, y, 1);
            }

            for (var y = 5; y < 8; y++)
            {
                for (var x = 0; x < 8; x += 2)
                    CreatePiece(x + y % 2, y, 2);
            }
        }
        else
            restartBtn.SetActive(false);
        FindMoves();
    }

    //Create a piece at x,y
    private void CreatePiece(int x, int y, int player)
    {
        Piece p;
        p = CreatePiecePrefab(player == 1 ? Player1Color : Player2Color);

        // NetSynced Move
        var pView = p.GetComponent<PhotonView>();
        pView.RPC("Move", RpcTarget.All, x, y);

        pView.RPC("SetPlayer", RpcTarget.All, player);
    }

    //Select the piece return true if a piece is selected
    private bool SelectPiece(int x, int y)
    {
        if (x < 0 || x > 7 || y < 0 || y > 7)
            return false;
        var p = GetActivePieces()[x, y];
        if (p == null) //if the selected square contains a piece
            return false;
        if (p.GetPlayer() != turn) //if not the player's piece
            return false;

        ClearHighlights();

        //Debug.Log("Selected " + x + " " + y);

        selected = p;
        //Debug.Log(selected.GetMovesNum());
        if (selected.GetMovesNum() > 0) //highlight piece if move is possible
        {
            selected.Select(true);
            DisplayMoves();
            return true;
        }

        selected.Select(false);
        selected = null;
        return false;
    }

    private void CheckWin()
    {
        var p1Count = 0;
        var p1MovesCount = 0;
        var p2Count = 0;
        var p2MovesCount = 0;
        var pieces = GetActivePieces();

        for (var i = 0; i < 8; i++)
        {
            for (var j = 0; j < 8; j++)
            {
                var p = pieces[i, j];
                if (p != null)
                {
                    if (p.GetPlayer() == 1)
                    {
                        p1Count += 1;
                        p1MovesCount += p.GetMovesNum();
                    }
                    else if (p.GetPlayer() == 2)
                    {
                        p2Count += 1;
                        p2MovesCount += p.GetMovesNum();
                    }
                }
            }
        }

        //if no pieces left or no available moves
        if (p2Count == 0 || (p2MovesCount == 0 && turn == 2)) //p1 win
            pv.RPC("EndGame", RpcTarget.All, 1);
        else if (p1Count == 0 || (p1MovesCount == 0 && turn == 1)) //p2 win
            pv.RPC("EndGame", RpcTarget.All, 2);

        ClearMoves();
    }

    private void DebugWin()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            pv.RPC("EndGame", RpcTarget.All, 1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            pv.RPC("EndGame", RpcTarget.All, 2);
        }
    }

    //After a person wins announce to both players 
    [PunRPC]
    private void EndGame(int player)
    {
        gameCompleted = true;
        isClickable = false;
        winGUI.SetActive(true);

        winText.text = "Player " + ((player == 1) ? 1 : 2) + " wins";
        //Debug.Log("P" + player + " won / " + ((player == 1) ? player1Color : player2Color));
    }

    private void FindMultiCapture(int x, int y, int dx, int dy)
    {
        multiCapture = false;
        var pieces = GetActivePieces();

        var adjSquareL = CheckSquare(x - 1, y + dy);
        var jumpSquareL = CheckSquare(x - 2, y + (2 * dy));
        var adjSquareR = CheckSquare(x + 1, y + dy);
        var jumpSquareR = CheckSquare(x + 2, y + (2 * dy));

        if (adjSquareL == 1 && jumpSquareL == 0)
        {
            var mL = CreateMovePrefab("Move");
            mL.MoveObj(x - 2, y + (2 * dy));

            mL.SetPriority(1);
            mL.SetCapture(pieces[x - 1, y + dy]);

            selected.AddMove(mL);
            multiCapture = true;
        }

        if (adjSquareR == 1 && jumpSquareR == 0)
        {
            var mR = CreateMovePrefab("Move");
            mR.MoveObj(x + 2, y + (2 * dy));

            mR.SetPriority(1);
            mR.SetCapture(pieces[x + 1, y + dy]);

            selected.AddMove(mR);
            multiCapture = true;
        }

        if (selected.GetKing())
        {
            var adjSquareB = CheckSquare(x + dx, y - dy);
            var jumpSquareB = CheckSquare(x + (2 * dx), y - (2 * dy));

            if (adjSquareB == 1 && jumpSquareB == 0)
            {
                var mB = CreateMovePrefab("Move");

                mB.MoveObj(x + 2 * dx, y - (2 * dy));

                mB.SetPriority(1);
                mB.SetCapture(pieces[x + dx, y - dy]);
                selected.AddMove(mB);
                multiCapture = true;
            }
        }
        HighlightMovable();
    }

    //Display all possible moves of selected piece
    private void DisplayMoves()
    {
        var moves = selected.GetMoves();
        for (var i = 0; i < selected.GetMovesNum(); i++)
        {
            var h = CreateMovePrefab("Highlight");
            var x = moves[i].GetX();
            var y = moves[i].GetY();
            var capture = moves[i].GetCapture();

            h.MoveObj(x, y);

            h.SetCapture(capture);
            highlights.Add(h);
        }
    }

    //Clear highlighted squares
    private void ClearHighlights()
    {
        foreach (var h in highlights)
            Destroy(h.gameObject);

        highlights.Clear();
    }

    //Find all possible moves for all pieces
    private void FindMoves()
    {
        priority = 0;
        var movablePieces = new List<Piece>();
        var pieces = GetActivePieces();
        for (var i = 0; i < 8; i++)
        {
            for (var j = 0; j < 8; j++)
            {
                var p = pieces[i, j];
                if (p == null)
                    continue;
                p.ClearMoves();

                var player = p.GetPlayer();
                if (player != turn)
                    continue;

                var up = 1;
                var dn = -1;
                if (player == 2)
                {
                    up = -1;
                    dn = 1;
                }

                //move forwards
                CheckDirection(p, i, j, dn, up);
                CheckDirection(p, i, j, up, up);

                if (p.GetKing()) //move backwards if the piece is a king
                {
                    CheckDirection(p, i, j, dn, dn);
                    CheckDirection(p, i, j, up, dn);
                }

                //If a capture move is available, keep only capture moves
                var prio = p.GetPriority();
                if (prio > priority)
                {
                    foreach (Piece piece in movablePieces)
                        piece.ClearMoves();

                    movablePieces.Clear();
                    priority = prio;
                }

                if (prio >= priority)
                    movablePieces.Add(p);
                else
                    p.ClearMoves();
            }
        }
        if ((IsPlayer1() ? 1 : 2) == turn)
            HighlightMovable();
    }

    private void HighlightMovable()
    {
        var pieces = GetActivePieces();
        for (var i = 0; i < 8; i++)
        {
            for (var j = 0; j < 8; j++)
            {
                var p = pieces[i, j];
                if (p == null)
                    continue;
                if (p.GetMovesNum() == 0)
                    p.HighlightPiece(false);
                else
                    p.HighlightPiece(true);
            }
        }
    }

    private Move CreateMovePrefab(string c)
    {
        GameObject go;
        if (c.Equals("Highlight"))
        {
            go = Instantiate(highlightPrefab, transform.position, Quaternion.identity);
            go.transform.parent = transform.Find("Moves").transform;
            return go.GetComponent<Move>();
        }
        go = Instantiate(movePrefab, transform.position, Quaternion.identity);
        go.transform.parent = transform.Find("Moves").transform;
        return go.GetComponent<Move>();
    }

    private Piece CreatePiecePrefab(string c)
    {
        GameObject go;
        PhotonView pView;
        if (c.Equals("Light"))
        {
            go = PhotonNetwork.Instantiate("LightPiece", transform.position, Quaternion.identity);
            pView = go.GetComponent<PhotonView>();
            pView.RPC("SetParent", RpcTarget.All, pv.ViewID);
            return go.GetComponent<Piece>();
        }
        go = PhotonNetwork.Instantiate("DarkPiece", transform.position, Quaternion.identity);
        pView = go.GetComponent<PhotonView>();
        pView.RPC("SetParent", RpcTarget.All, pv.ViewID);
        return go.GetComponent<Piece>();
    }

    private void CheckDirection(Piece p, int x, int y, int dx, int dy)
    {
        var pieces = GetActivePieces();
        var m = CreateMovePrefab("Move");

        var adjSquare = CheckSquare(x + dx, y + dy);
        var jumpSquare = CheckSquare(x + (2 * dx), y + (2 * dy));


        if (adjSquare == 0) //Move
        {
            m.MoveObj(x + dx, y + dy);

            m.SetPriority(0);
            p.AddMove(m);
        }
        else if (adjSquare == 1 && jumpSquare == 0) //Capture
        {
            m.MoveObj(x + (2 * dx), y + (2 * dy));

            m.SetPriority(1);
            m.SetCapture(pieces[x + dx, y + dy]);
            p.AddMove(m);
        }
        else //No possible move
        {
            Destroy(m.gameObject);
        }
    }

    //Check what is on square at (x,y)
    private int CheckSquare(int x, int y)
    {
        Piece[,] pieces = GetActivePieces();
        if (x < 0 || x > 7 || y < 0 || y > 7) //out of board
            return -1;
        if (pieces[x, y] == null) //no piece
            return 0;
        if (pieces[x, y].GetPlayer() == turn) //player's piece
            return -1;
        return 1; //opponent's piece
    }

    //Display the current board layout in console
    /*private void DebugBoard()
    {
        var pieces = GetActivePieces();
        for (var j = 7; j >= 0; j--)
        {
            for (var i = 0; i < 8; i++)
            {
                if (pieces[i, j] != null)
                    str += "P";
                else
                    str += "O";
            }
            str += "\n";
        }
        Debug.Log(str);
    }*/
}