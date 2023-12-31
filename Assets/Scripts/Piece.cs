﻿using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class Piece : Square
{
    private readonly Vector2 boardOffset = new Vector2(-4.0f, -4.0f);
    private bool king;
    private List<Move> moves = new List<Move>();
    private readonly Vector2 pieceOffset = new Vector2(0.5f, 0.5f);
    private int player;
    public GameObject kingPrefab;
    public GameObject selected;
    public GameObject movable;
    public int GetPlayer()
    {
        return player;
    }

    [PunRPC]
    public void SetPlayer(int player)
    {
        this.player = player;
    }

    public bool GetKing()
    {
        return king;
    }


    [PunRPC]
    public void Promote()
    {
        king = true;
        kingPrefab.SetActive(true);
        if (!Board.Instance.IsPlayer1())
        {
            kingPrefab.transform.localRotation = Quaternion.Euler(0, 0, 180);
        }
    }

    public void Select(bool state)
    {
        selected.SetActive(state);
    }

    public void HighlightPiece(bool state)
    {
        movable.SetActive(state);
    }

    public List<Move> GetMoves()
    {
        return moves;
    }

    public void AddMove(Move move)
    {
        var prio = move.GetPriority();
        if (prio > Priority) //Force capture
        {
            foreach (var m in moves)
            {
                Destroy(m.gameObject);
            }
            moves.Clear();
            Priority = prio;
        }

        if (prio >= Priority)
            moves.Add(move);
        else
            Destroy(move.gameObject);
    }

    public void ClearMoves()
    {
        foreach (var m in moves)
        {
            Destroy(m.gameObject);
        }
        moves.Clear();
        Priority = 0;
    }

    public int GetMovesNum()
    {
        return moves.Count;
    }

    [PunRPC]
    public void Move(int xVal, int yVal)
    {
        X = xVal;
        Y = yVal;
        transform.localPosition = (new Vector2(xVal, yVal) + boardOffset + pieceOffset)/10;
    }

    [PunRPC]
    public void DestroyPiece()
    {
        Destroy(gameObject);
    }

    [PunRPC]
    public void SetParent(int pvID)
    {
        PhotonView pv = PhotonView.Find(pvID);
        transform.parent = pv.gameObject.transform.Find("Pieces").transform;
    }
}